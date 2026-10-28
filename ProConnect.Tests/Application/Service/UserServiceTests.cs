using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ProConnect.Application.Interface;
using ProConnect.Application.Service;
using ProConnect.Domain.Config;
using ProConnect.Domain.Request;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Repository;

namespace ProConnect.Tests.Application.Service
{
    /// <summary>
    /// Covers the UserService methods/branches that depend only on interface collaborators
    /// (IUserRepository, IRefreshTokenRepository, ITokenService) and never touch the concrete Cache
    /// field - RefreshToken, Logout, DeactivateAccount, and SignUp's early duplicate-sub return. Those
    /// are passed null for the Cache constructor parameter below (safe here: Cache is a concrete class
    /// with no null-check in UserService's constructor, and none of these code paths ever reach
    /// FetchLinkedInProfile, the only method that dereferences _cache). Branches that DO reach
    /// FetchLinkedInProfile (duplicate-email check, null/empty scraped profile) are deliberately not
    /// covered here - exercising those would require either a real Cache wrapping a mocked ICache or an
    /// in-memory ICache fake, which is a larger investment left for a follow-up.
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

        private UserService CreateService()
        {
            var proConnectConfig = new ProConnectConfig
            {
                jwtConfig = new JwtConfig { refreshtokenexpiryindays = 30 }
            };

            return new UserService(
                _userRepository.Object,
                _httpClientUtilities.Object,
                _profileProvider.Object,
                _geocodingProvider.Object,
                new Mock<ILogger<UserService>>().Object,
                cache: null!,
                _tokenService.Object,
                _matchService.Object,
                _refreshTokenRepository.Object,
                Options.Create(proConnectConfig),
                new CacheExpiryConfig());
        }

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
    }
}
