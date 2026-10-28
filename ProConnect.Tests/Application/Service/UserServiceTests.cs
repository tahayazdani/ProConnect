using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ProConnect.Application.Interface;
using ProConnect.Application.Service;
using ProConnect.Domain.Config;
using ProConnect.Domain.Const;
using ProConnect.Domain.Exception;
using ProConnect.Domain.Model;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Repository;

namespace ProConnect.Tests.Application.Service
{
    /// <summary>
    /// Covers UserService end to end, including the branches that go through FetchLinkedInProfile
    /// (SignUp's success/empty-profile/duplicate-email paths, GetLinkedInProfile) - CreateService below
    /// wires a real Cache around a mocked ICache (CacheAsideConfig.enablecacheaside left false, so
    /// Cache.Set no-ops without needing ICache.Set configured) rather than passing cache: null! as this
    /// file used to, since those branches need Cache.Get to actually run. SetupCacheMiss configures the
    /// one ICache call Cache.Get always makes (GetCacheItem) to report a miss, so every test here exercises
    /// FetchLinkedInProfile falling through to IProfileProvider.GetProfile exactly once per publicIdentifier,
    /// same as a cold cache in production.
    /// </summary>
    public class UserServiceTests
    {
        private readonly Mock<IUserRepository> _userRepository = new();
        private readonly Mock<IHttpClientUtilities> _httpClientUtilities = new();
        private readonly Mock<IProfileProvider> _profileProvider = new();
        private readonly Mock<IGeocodingProvider> _geocodingProvider = new();
        private readonly Mock<ITokenService> _tokenService = new();
        private readonly Mock<IMatchService> _matchService = new();
        private readonly Mock<IRefreshTokenRepository> _refreshTokenRepository = new();
        private readonly Mock<INotificationService> _notificationService = new();
        private readonly Mock<ICache> _cacheBacking = new();

        private UserService CreateService()
        {
            var proConnectConfig = new ProConnectConfig
            {
                jwtConfig = new JwtConfig { refreshtokenexpiryindays = 30 },
                linkedInApplicationConfig = new LinkedInApplicationConfig
                {
                    client_id = "client-id",
                    client_secret = "client-secret",
                    redirect_url = "https://app.example.com/callback",
                    linkedin_baseurl = "https://www.linkedin.com/oauth/v2/",
                    linkedin_api_baseurl = "https://api.linkedin.com/v2/",
                    linkedin_accesstokenpath = "accessToken",
                    linkedin_userinfopath = "userinfo"
                }
            };

            var cache = new Cache(
                new Mock<ILogger<Cache>>().Object,
                _cacheBacking.Object,
                new CacheAsideConfig { enablecacheaside = false });

            return new UserService(
                _userRepository.Object,
                _httpClientUtilities.Object,
                _profileProvider.Object,
                _geocodingProvider.Object,
                new Mock<ILogger<UserService>>().Object,
                cache,
                _tokenService.Object,
                _matchService.Object,
                _refreshTokenRepository.Object,
                _notificationService.Object,
                Options.Create(proConnectConfig),
                new CacheExpiryConfig { personprofile = 604800 });
        }

        // Every FetchLinkedInProfile call starts with Cache.Get calling ICache.GetCacheItem - configuring
        // it to report "not found" means Cache.Get always falls through to IProfileProvider.GetProfile,
        // same as a cold cache. Cache.Set is a no-op here (enablecacheaside = false above), so there is
        // nothing else on _cacheBacking that needs setting up for these tests to reach the real provider.
        private void SetupCacheMiss(string publicIdentifier) =>
            _cacheBacking
                .Setup(c => c.GetCacheItem<CacheItem<PersonProfileResponse>>(publicIdentifier, CacheRegion.PERSONPROFILE))
                .ReturnsAsync(new CacheItem<CacheItem<PersonProfileResponse>> { Exists = false });

        [Fact]
        public async Task RefreshToken_WithEmptyToken_ReturnsFailureWithoutTouchingRepositories()
        {
            var service = CreateService();

            var response = await service.RefreshToken(string.Empty);

            Assert.False(response.issuccessfull);
            Assert.Null(response.body);
            _refreshTokenRepository.Verify(r => r.GetActiveByHash(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RefreshToken_WithUnknownOrExpiredHash_ReturnsGenericFailure()
        {
            _tokenService.Setup(t => t.HashToken("raw-token")).Returns("hashed-token");
            _refreshTokenRepository.Setup(r => r.GetActiveByHash("hashed-token"))
                .ReturnsAsync((RefreshTokenInfo)null!);

            var service = CreateService();

            var response = await service.RefreshToken("raw-token");

            Assert.False(response.issuccessfull);
            Assert.Null(response.body);
            Assert.Equal("Refresh token is invalid or has expired. Please sign in again.", response.errormessage);
            _refreshTokenRepository.Verify(r => r.Rotate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public async Task RefreshToken_WhenUserNoLongerExists_ReturnsGenericFailureAndDoesNotRotate()
        {
            var userid = Guid.NewGuid();
            _tokenService.Setup(t => t.HashToken("raw-token")).Returns("hashed-token");
            _refreshTokenRepository.Setup(r => r.GetActiveByHash("hashed-token"))
                .ReturnsAsync(new RefreshTokenInfo { userid = userid, tokenhash = "hashed-token", expiresat_utc = DateTime.UtcNow.AddDays(1) });
            _userRepository.Setup(u => u.GetById(userid)).ReturnsAsync((UserRequest)null!);

            var service = CreateService();

            var response = await service.RefreshToken("raw-token");

            Assert.False(response.issuccessfull);
            Assert.Equal("Refresh token is invalid or has expired. Please sign in again.", response.errormessage);
            _refreshTokenRepository.Verify(r => r.Rotate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public async Task RefreshToken_WithValidToken_RotatesAndReturnsNewPair()
        {
            var userid = Guid.NewGuid();
            var user = new UserRequest { userid = userid, public_identifier = "jdoe", emailaddress = "jdoe@example.com" };

            _tokenService.Setup(t => t.HashToken("raw-token")).Returns("old-hash");
            _tokenService.Setup(t => t.HashToken("new-raw-token")).Returns("new-hash");
            _tokenService.Setup(t => t.GenerateRefreshToken()).Returns("new-raw-token");
            _tokenService.Setup(t => t.GenerateToken(userid, "jdoe", "jdoe@example.com")).Returns("new-access-token");

            _refreshTokenRepository.Setup(r => r.GetActiveByHash("old-hash"))
                .ReturnsAsync(new RefreshTokenInfo { userid = userid, tokenhash = "old-hash", expiresat_utc = DateTime.UtcNow.AddDays(1) });
            _userRepository.Setup(u => u.GetById(userid)).ReturnsAsync(user);

            var service = CreateService();

            var response = await service.RefreshToken("raw-token");

            Assert.True(response.issuccessfull);
            Assert.NotNull(response.body);
            Assert.Equal("new-access-token", response.body!.token);
            Assert.Equal("new-raw-token", response.body!.refreshtoken);

            _refreshTokenRepository.Verify(r => r.Rotate("old-hash", "new-hash", It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task Logout_WithToken_RevokesIt()
        {
            _tokenService.Setup(t => t.HashToken("raw-token")).Returns("hashed-token");

            var service = CreateService();

            var response = await service.Logout("raw-token");

            Assert.True(response.issuccessfull);
            _refreshTokenRepository.Verify(r => r.Revoke("hashed-token"), Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Logout_WithoutToken_NoOpsCleanly(string? refreshtoken)
        {
            var service = CreateService();

            var response = await service.Logout(refreshtoken!);

            Assert.True(response.issuccessfull);
            _refreshTokenRepository.Verify(r => r.Revoke(It.IsAny<string>()), Times.Never);
            _tokenService.Verify(t => t.HashToken(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task DeactivateAccount_WhenUserFound_RevokesAllRefreshTokens()
        {
            var userid = Guid.NewGuid();
            _userRepository.Setup(u => u.Deactivate(userid)).ReturnsAsync(true);

            var service = CreateService();

            var response = await service.DeactivateAccount(userid);

            Assert.True(response.issuccessfull);
            _refreshTokenRepository.Verify(r => r.RevokeAllForUser(userid), Times.Once);
        }

        [Fact]
        public async Task DeactivateAccount_WhenUserNotFound_DoesNotRevokeAnything()
        {
            var userid = Guid.NewGuid();
            _userRepository.Setup(u => u.Deactivate(userid)).ReturnsAsync(false);

            var service = CreateService();

            var response = await service.DeactivateAccount(userid);

            Assert.False(response.issuccessfull);
            _refreshTokenRepository.Verify(r => r.RevokeAllForUser(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task SignUp_WithSubThatAlreadyHasAnAccount_ReturnsCleanFailureWithoutScrapingOrIssuingTokens()
        {
            var request = new SignUpRequest { sub = "existing-sub", public_identifier = "jdoe" };
            _userRepository.Setup(u => u.GetBySubForAuthentication("existing-sub"))
                .ReturnsAsync(new UserRequest { userid = Guid.NewGuid(), sub = "existing-sub" });

            var service = CreateService();

            var response = await service.SignUp(request);

            Assert.False(response.issuccessfull);
            Assert.Equal("A user with this account already exists. Please sign in instead.", response.errormessage);
            _profileProvider.Verify(p => p.GetProfile(It.IsAny<string>()), Times.Never);
            _refreshTokenRepository.Verify(r => r.Create(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public async Task SignUp_WithNullRequest_ReturnsFailureWithoutTouchingAnything()
        {
            var service = CreateService();

            var response = await service.SignUp(null!);

            Assert.False(response.issuccessfull);
            Assert.Equal("Request body is required.", response.errormessage);
            _profileProvider.Verify(p => p.GetProfile(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SignUp_WhenScrapeReturnsNull_ReturnsCleanFailure()
        {
            var request = new SignUpRequest { public_identifier = "jdoe", email = "jdoe@example.com" };
            SetupCacheMiss("jdoe");
            _profileProvider.Setup(p => p.GetProfile("jdoe")).ReturnsAsync((PersonProfileResponse)null!);

            var service = CreateService();

            var response = await service.SignUp(request);

            Assert.False(response.issuccessfull);
            Assert.Equal(
                "We couldn't retrieve a valid LinkedIn profile for this identifier. Please check it and try again.",
                response.errormessage);
            _userRepository.Verify(u => u.SignUp(It.IsAny<UserRequest>()), Times.Never);
        }

        [Fact]
        public async Task SignUp_WhenScrapeHasNoUsableName_ReturnsCleanFailure()
        {
            var request = new SignUpRequest { public_identifier = "jdoe", email = "jdoe@example.com" };
            SetupCacheMiss("jdoe");
            _profileProvider.Setup(p => p.GetProfile("jdoe"))
                .ReturnsAsync(new PersonProfileResponse { public_identifier = "jdoe" });

            var service = CreateService();

            var response = await service.SignUp(request);

            Assert.False(response.issuccessfull);
            Assert.Equal(
                "We couldn't retrieve a valid LinkedIn profile for this identifier. Please check it and try again.",
                response.errormessage);
            _userRepository.Verify(u => u.SignUp(It.IsAny<UserRequest>()), Times.Never);
        }

        [Fact]
        public async Task SignUp_WhenEmailAlreadyHasAnAccount_ReturnsCleanFailureWithoutPersisting()
        {
            var request = new SignUpRequest { public_identifier = "jdoe", email = "jdoe@example.com", birth_date = new date { day = 1, month = 1, year = 1990 } };
            SetupCacheMiss("jdoe");
            _profileProvider.Setup(p => p.GetProfile("jdoe"))
                .ReturnsAsync(new PersonProfileResponse { public_identifier = "jdoe", first_name = "Jane" });
            _userRepository.Setup(u => u.GetByEmailForAuthentication("jdoe@example.com"))
                .ReturnsAsync(new UserRequest { userid = Guid.NewGuid(), emailaddress = "jdoe@example.com" });

            var service = CreateService();

            var response = await service.SignUp(request);

            Assert.False(response.issuccessfull);
            Assert.Equal("A user with this email already exists. Please sign in instead.", response.errormessage);
            _userRepository.Verify(u => u.SignUp(It.IsAny<UserRequest>()), Times.Never);
        }

        [Fact]
        public async Task SignUp_WithNewProfile_PersistsAndIssuesTokenPair()
        {
            var request = new SignUpRequest { public_identifier = "jdoe", email = "jdoe@example.com", sub = "new-sub", birth_date = new date { day = 1, month = 1, year = 1990 } };
            SetupCacheMiss("jdoe");

            var scraped = new PersonProfileResponse
            {
                public_identifier = "jdoe",
                first_name = "Jane",
                last_name = "Doe",
                full_name = "Jane Doe"
            };
            _profileProvider.Setup(p => p.GetProfile("jdoe")).ReturnsAsync(scraped);

            _userRepository.Setup(u => u.SignUp(It.IsAny<UserRequest>()))
                .ReturnsAsync((UserRequest r) => new SignUpResponse { userid = r.userid, public_identifier = r.public_identifier });

            _tokenService.Setup(t => t.GenerateToken(It.IsAny<Guid>(), "jdoe", "jdoe@example.com")).Returns("access-token");
            _tokenService.Setup(t => t.GenerateRefreshToken()).Returns("refresh-token");

            var service = CreateService();

            var response = await service.SignUp(request);

            Assert.True(response.issuccessfull);
            Assert.NotNull(response.body);
            Assert.Equal("access-token", response.body!.token);
            Assert.Equal("refresh-token", response.body!.refreshtoken);
            Assert.Equal("jdoe", response.body!.public_identifier);

            _userRepository.Verify(u => u.SignUp(It.Is<UserRequest>(r => r.public_identifier == "jdoe" && r.sub == "new-sub")), Times.Once);
            _refreshTokenRepository.Verify(r => r.Create(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Once);
        }

        [Theory]
        [InlineData("email", "A user with this email already exists. Please sign in instead.")]
        [InlineData("sub", "A user with this account already exists. Please sign in instead.")]
        public async Task SignUp_WhenRepositoryThrowsDuplicateUserException_MapsToCleanFailure(string conflictField, string expectedMessage)
        {
            var request = new SignUpRequest { public_identifier = "jdoe", email = "jdoe@example.com", birth_date = new date { day = 1, month = 1, year = 1990 } };
            SetupCacheMiss("jdoe");
            _profileProvider.Setup(p => p.GetProfile("jdoe"))
                .ReturnsAsync(new PersonProfileResponse { public_identifier = "jdoe", first_name = "Jane" });
            _userRepository.Setup(u => u.SignUp(It.IsAny<UserRequest>()))
                .ThrowsAsync(new DuplicateUserException(conflictField));

            var service = CreateService();

            var response = await service.SignUp(request);

            Assert.False(response.issuccessfull);
            Assert.Equal(expectedMessage, response.errormessage);
        }

        [Fact]
        public async Task GetLinkedInProfile_WithEmptyIdentifier_ReturnsFailureWithoutScraping()
        {
            var service = CreateService();

            var response = await service.GetLinkedInProfile(string.Empty);

            Assert.False(response.issuccessfull);
            Assert.Equal("public_identifier is required.", response.errormessage);
            _profileProvider.Verify(p => p.GetProfile(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetLinkedInProfile_WhenNotFound_ReturnsCleanFailure()
        {
            SetupCacheMiss("jdoe");
            _profileProvider.Setup(p => p.GetProfile("jdoe")).ReturnsAsync((PersonProfileResponse)null!);

            var service = CreateService();

            var response = await service.GetLinkedInProfile("jdoe");

            Assert.False(response.issuccessfull);
            Assert.Equal("LinkedIn profile could not be found for 'jdoe'.", response.errormessage);
        }

        [Fact]
        public async Task GetLinkedInProfile_WhenFound_ReturnsProfile()
        {
            SetupCacheMiss("jdoe");
            var scraped = new PersonProfileResponse { public_identifier = "jdoe", first_name = "Jane" };
            _profileProvider.Setup(p => p.GetProfile("jdoe")).ReturnsAsync(scraped);

            var service = CreateService();

            var response = await service.GetLinkedInProfile("jdoe");

            Assert.True(response.issuccessfull);
            Assert.Same(scraped, response.body);
        }

        [Fact]
        public async Task GenerateConsentLinkedInLogInPage_ReturnsHtmlContentFromLinkedIn()
        {
            _httpClientUtilities
                .Setup(h => h.GetStringAsync("https://www.linkedin.com/oauth/v2/", It.IsAny<string>(), null, null))
                .ReturnsAsync("<html>consent</html>");

            var service = CreateService();

            var response = await service.GenerateConsentLinkedInLogInPage();

            Assert.True(response.issuccessfull);
            Assert.Equal("<html>consent</html>", response.body!.htmlcontent);
        }

        [Fact]
        public async Task LinkedInAuthentication_WhenTokenExchangeThrows_ReturnsGenericFailure()
        {
            _httpClientUtilities
                .Setup(h => h.PostAsync<TokenResponse>(It.IsAny<string>(), It.IsAny<object>(), null, null))
                .ThrowsAsync(new HttpRequestException("401"));

            var service = CreateService();

            var response = await service.LinkedInAuthentication("bad-code", "state");

            Assert.False(response.issuccessfull);
            Assert.Equal("LinkedIn authentication failed. Please try signing in again.", response.errormessage);
            _userRepository.Verify(u => u.GetBySubForAuthentication(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task LinkedInAuthentication_WhenAccessTokenMissing_ReturnsCleanFailure()
        {
            _httpClientUtilities
                .Setup(h => h.PostAsync<TokenResponse>(It.IsAny<string>(), It.IsAny<object>(), null, null))
                .ReturnsAsync(new TokenResponse { access_token = string.Empty });

            var service = CreateService();

            var response = await service.LinkedInAuthentication("code", "state");

            Assert.False(response.issuccessfull);
            Assert.Equal("Failed to obtain an access token from LinkedIn.", response.errormessage);
        }

        [Fact]
        public async Task LinkedInAuthentication_WhenSubMissing_ReturnsCleanFailure()
        {
            _httpClientUtilities
                .Setup(h => h.PostAsync<TokenResponse>(It.IsAny<string>(), It.IsAny<object>(), null, null))
                .ReturnsAsync(new TokenResponse { access_token = "access-token" });
            _httpClientUtilities
                .Setup(h => h.GetAsync<SignInResponse>(It.IsAny<string>(), It.IsAny<string>(), "access-token", null))
                .ReturnsAsync(new SignInResponse { sub = string.Empty });

            var service = CreateService();

            var response = await service.LinkedInAuthentication("code", "state");

            Assert.False(response.issuccessfull);
            Assert.Equal("The user is not exist in LinkedIn.", response.errormessage);
        }

        [Fact]
        public async Task LinkedInAuthentication_WhenUserHasNotSignedUp_ReturnsCleanFailure()
        {
            _httpClientUtilities
                .Setup(h => h.PostAsync<TokenResponse>(It.IsAny<string>(), It.IsAny<object>(), null, null))
                .ReturnsAsync(new TokenResponse { access_token = "access-token" });
            _httpClientUtilities
                .Setup(h => h.GetAsync<SignInResponse>(It.IsAny<string>(), It.IsAny<string>(), "access-token", null))
                .ReturnsAsync(new SignInResponse { sub = "li-sub" });
            _userRepository.Setup(u => u.GetBySubForAuthentication("li-sub")).ReturnsAsync((UserRequest)null!);

            var service = CreateService();

            var response = await service.LinkedInAuthentication("code", "state");

            Assert.False(response.issuccessfull);
            Assert.Equal("The user has not signed up yet.", response.errormessage);
            _refreshTokenRepository.Verify(r => r.Create(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public async Task LinkedInAuthentication_WhenExistingUserIsDeactivated_ReactivatesAndSignsIn()
        {
            var userid = Guid.NewGuid();
            _httpClientUtilities
                .Setup(h => h.PostAsync<TokenResponse>(It.IsAny<string>(), It.IsAny<object>(), null, null))
                .ReturnsAsync(new TokenResponse { access_token = "access-token" });
            _httpClientUtilities
                .Setup(h => h.GetAsync<SignInResponse>(It.IsAny<string>(), It.IsAny<string>(), "access-token", null))
                .ReturnsAsync(new SignInResponse { sub = "li-sub" });
            _userRepository.Setup(u => u.GetBySubForAuthentication("li-sub"))
                .ReturnsAsync(new UserRequest { userid = userid, sub = "li-sub", public_identifier = "jdoe", emailaddress = "jdoe@example.com", is_active = false });
            _tokenService.Setup(t => t.GenerateToken(userid, "jdoe", "jdoe@example.com")).Returns("new-access-token");
            _tokenService.Setup(t => t.GenerateRefreshToken()).Returns("new-refresh-token");

            var service = CreateService();

            var response = await service.LinkedInAuthentication("code", "state");

            Assert.True(response.issuccessfull);
            Assert.Equal("new-access-token", response.body!.token);
            _userRepository.Verify(u => u.Reactivate(userid), Times.Once);
        }

        [Fact]
        public async Task LinkedInAuthentication_WhenExistingUserIsActive_SignsInWithoutReactivating()
        {
            var userid = Guid.NewGuid();
            _httpClientUtilities
                .Setup(h => h.PostAsync<TokenResponse>(It.IsAny<string>(), It.IsAny<object>(), null, null))
                .ReturnsAsync(new TokenResponse { access_token = "access-token" });
            _httpClientUtilities
                .Setup(h => h.GetAsync<SignInResponse>(It.IsAny<string>(), It.IsAny<string>(), "access-token", null))
                .ReturnsAsync(new SignInResponse { sub = "li-sub" });
            _userRepository.Setup(u => u.GetBySubForAuthentication("li-sub"))
                .ReturnsAsync(new UserRequest { userid = userid, sub = "li-sub", public_identifier = "jdoe", emailaddress = "jdoe@example.com", is_active = true });
            _tokenService.Setup(t => t.GenerateToken(userid, "jdoe", "jdoe@example.com")).Returns("new-access-token");
            _tokenService.Setup(t => t.GenerateRefreshToken()).Returns("new-refresh-token");

            var service = CreateService();

            var response = await service.LinkedInAuthentication("code", "state");

            Assert.True(response.issuccessfull);
            _userRepository.Verify(u => u.Reactivate(It.IsAny<Guid>()), Times.Never);
            _refreshTokenRepository.Verify(r => r.Create(userid, It.IsAny<string>(), It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task SystemAuthentication_WhenUserNotFound_ReturnsCleanFailure()
        {
            var userid = Guid.NewGuid();
            _userRepository.Setup(u => u.GetById(userid)).ReturnsAsync((UserRequest)null!);

            var service = CreateService();

            var response = await service.SystemAuthentication(userid);

            Assert.False(response.issuccessfull);
            Assert.Equal("User not found.", response.errormessage);
        }

        [Fact]
        public async Task SystemAuthentication_WhenUserFound_ReturnsMappedProfile()
        {
            var userid = Guid.NewGuid();
            _userRepository.Setup(u => u.GetById(userid)).ReturnsAsync(new UserRequest
            {
                userid = userid,
                sub = "li-sub",
                emailaddress = "jdoe@example.com",
                full_name = "Jane Doe",
                first_name = "Jane",
                last_name = "Doe",
                profile_pic_url = "https://example.com/pic.png"
            });

            var service = CreateService();

            var response = await service.SystemAuthentication(userid);

            Assert.True(response.issuccessfull);
            Assert.Equal("li-sub", response.body!.sub);
            Assert.Equal("jdoe@example.com", response.body!.email);
            Assert.Equal("Jane Doe", response.body!.name);
        }

        [Fact]
        public async Task GetProfile_WhenUserNotFound_ReturnsCleanFailure()
        {
            var userid = Guid.NewGuid();
            _userRepository.Setup(u => u.GetById(userid)).ReturnsAsync((UserRequest)null!);

            var service = CreateService();

            var response = await service.GetProfile(userid);

            Assert.False(response.issuccessfull);
            Assert.Equal("User not found.", response.errormessage);
        }

        [Fact]
        public async Task GetProfile_WhenUserFound_ReturnsMappedProfile()
        {
            var userid = Guid.NewGuid();
            _userRepository.Setup(u => u.GetById(userid)).ReturnsAsync(new UserRequest
            {
                userid = userid,
                public_identifier = "jdoe",
                full_name = "Jane Doe"
            });

            var service = CreateService();

            var response = await service.GetProfile(userid);

            Assert.True(response.issuccessfull);
            Assert.Equal("jdoe", response.body!.public_identifier);
            Assert.Equal("Jane Doe", response.body!.full_name);
        }

        [Fact]
        public async Task UpdateProfile_WithNullRequest_ReturnsFailureWithoutTouchingRepository()
        {
            var service = CreateService();

            var response = await service.UpdateProfile(Guid.NewGuid(), null!);

            Assert.False(response.issuccessfull);
            Assert.Equal("Request body is required.", response.errormessage);
            _userRepository.Verify(u => u.GetById(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task UpdateProfile_WhenUserNotFound_ReturnsCleanFailure()
        {
            var userid = Guid.NewGuid();
            _userRepository.Setup(u => u.GetById(userid)).ReturnsAsync((UserRequest)null!);

            var service = CreateService();

            var response = await service.UpdateProfile(userid, new UpdateProfileRequest { first_name = "Jane" });

            Assert.False(response.issuccessfull);
            Assert.Equal("User not found.", response.errormessage);
        }

        [Fact]
        public async Task UpdateProfile_WhenRepositoryUpdateFails_ReturnsCleanFailure()
        {
            var userid = Guid.NewGuid();
            _userRepository.Setup(u => u.GetById(userid)).ReturnsAsync(new UserRequest { userid = userid, first_name = "Jane", city = "Austin" });
            _userRepository.Setup(u => u.Update(It.IsAny<UserRequest>())).ReturnsAsync((UserRequest)null!);

            var service = CreateService();

            var response = await service.UpdateProfile(userid, new UpdateProfileRequest { headline = "Engineer" });

            Assert.False(response.issuccessfull);
            Assert.Equal("Failed to update the profile.", response.errormessage);
        }

        [Fact]
        public async Task UpdateProfile_WithoutLocationChange_DoesNotReGeocode()
        {
            var userid = Guid.NewGuid();
            var existing = new UserRequest { userid = userid, first_name = "Jane", city = "Austin", state = "TX", country = "US" };
            _userRepository.Setup(u => u.GetById(userid)).ReturnsAsync(existing);
            _userRepository.Setup(u => u.Update(It.IsAny<UserRequest>())).ReturnsAsync((UserRequest r) => r);

            var service = CreateService();

            var response = await service.UpdateProfile(userid, new UpdateProfileRequest { headline = "Engineer", city = "Austin" });

            Assert.True(response.issuccessfull);
            Assert.Equal("Engineer", response.body!.headline);
            _geocodingProvider.Verify(g => g.Geocode(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task UpdateProfile_WithLocationChange_ReGeocodesAndPersistsNewCoordinates()
        {
            var userid = Guid.NewGuid();
            var existing = new UserRequest { userid = userid, first_name = "Jane", city = "Austin", state = "TX", country = "US" };
            _userRepository.Setup(u => u.GetById(userid)).ReturnsAsync(existing);
            _geocodingProvider.Setup(g => g.Geocode("Seattle", "TX", "US"))
                .ReturnsAsync(new GeoCoordinate { latitude = 47.6, longitude = -122.3 });
            _userRepository.Setup(u => u.Update(It.IsAny<UserRequest>())).ReturnsAsync((UserRequest r) => r);

            var service = CreateService();

            var response = await service.UpdateProfile(userid, new UpdateProfileRequest { city = "Seattle" });

            Assert.True(response.issuccessfull);
            _userRepository.Verify(u => u.Update(It.Is<UserRequest>(r => r.latitude == 47.6 && r.longitude == -122.3)), Times.Once);
        }

        [Fact]
        public async Task DeleteAccount_WhenUserNotFound_ReturnsCleanFailure()
        {
            var userid = Guid.NewGuid();
            _userRepository.Setup(u => u.DeleteAccount(userid)).ReturnsAsync(false);

            var service = CreateService();

            var response = await service.DeleteAccount(userid);

            Assert.False(response.issuccessfull);
            Assert.Equal("User not found.", response.errormessage);
        }

        [Fact]
        public async Task DeleteAccount_WhenUserFound_ReturnsSuccessAndDoesNotRevokeTokensItself()
        {
            var userid = Guid.NewGuid();
            _userRepository.Setup(u => u.DeleteAccount(userid)).ReturnsAsync(true);

            var service = CreateService();

            var response = await service.DeleteAccount(userid);

            Assert.True(response.issuccessfull);
            Assert.Equal("Deleted", response.body);
            // refresh_token's FK to users cascades the delete - DeleteAccount deliberately never calls
            // RevokeAllForUser itself, see its own remarks.
            _refreshTokenRepository.Verify(r => r.RevokeAllForUser(It.IsAny<Guid>()), Times.Never);
        }
    }
}
