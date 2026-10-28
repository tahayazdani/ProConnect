using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProConnect.Application.Interface;
using ProConnect.Domain.Config;
using ProConnect.Domain.Const;
using ProConnect.Domain.Exception;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Repository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace ProConnect.Application.Service
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IHttpClientUtilities _httpClientUtilities;
        private readonly IProfileProvider _profileProvider;
        private readonly IGeocodingProvider _geocodingProvider;
        private readonly ProConnectConfig _proConnectConfig;
        private readonly CacheExpiryConfig _cacheExpiryConfig;
        private readonly ILogger<UserService> _logger;
        private readonly Cache _cache;
        private readonly ITokenService _tokenService;
        private readonly IMatchService _matchService;
        private readonly IRefreshTokenRepository _refreshTokenRepository;

        public UserService(IUserRepository userRepository,
                           IHttpClientUtilities httpClientUtilities,
                           IProfileProvider profileProvider,
                           IGeocodingProvider geocodingProvider,
                           ILogger<UserService> logger,
                           Cache cache,
                           ITokenService tokenService,
                           IMatchService matchService,
                           IRefreshTokenRepository refreshTokenRepository,
                           IOptions<ProConnectConfig> proConnectConfig,
                           CacheExpiryConfig cacheExpiryConfig
                           )
        {
            _userRepository = userRepository;
            _httpClientUtilities = httpClientUtilities;
            _profileProvider = profileProvider;
            _geocodingProvider = geocodingProvider;
            _logger = logger;
            _cache = cache;
            _tokenService = tokenService;
            _matchService = matchService;
            _refreshTokenRepository = refreshTokenRepository;
            _proConnectConfig = proConnectConfig.Value;
            _cacheExpiryConfig = cacheExpiryConfig;
        }

        // Shared by SignUp and LinkedInAuthentication: issues the access token exactly as before, plus a
        // brand-new refresh token that gets persisted (hashed - see RefreshTokenEntity) so the caller can
        // later redeem it via RefreshToken instead of forcing a full LinkedIn re-sign-in every time the
        // short-lived access token expires.
        private async Task<(string accesstoken, string refreshtoken)> IssueTokenPair(Guid userid, string public_identifier, string email)
        {
            var accesstoken = _tokenService.GenerateToken(userid, public_identifier, email);
            var refreshtoken = _tokenService.GenerateRefreshToken();

            await _refreshTokenRepository.Create(
                userid,
                _tokenService.HashToken(refreshtoken),
                DateTime.UtcNow.AddDays(_proConnectConfig.jwtConfig.refreshtokenexpiryindays));

            return (accesstoken, refreshtoken);
        }

        // Fire-and-forget: UserService/MatchService/Cache are all registered Singleton (see Program.cs),
        // so capturing _matchService in a detached Task is safe - same reasoning as
        // MatchService.TriggerBackgroundRefill. Called right after every session-establishing event
        // (sign up / LinkedIn sign-in / "/me") so the caller's response (token, profile, etc.) is never
        // delayed by a Redis/Postgres round trip that only benefits a *later* request. WarmRecommendations
        // itself already swallows and logs its own failures, so there is nothing to catch here.
        private static void TriggerRecommendationWarmUp(IMatchService matchService, Guid userid) =>
            _ = Task.Run(() => matchService.WarmRecommendations(userid));

        // Postgres's unique index on emailaddress (and every in-code comparison we do against it) is
        // case-sensitive and whitespace-sensitive by default, but email addresses are not - two users
        // typing "Test@Example.com" and "test@example.com" are the same mailbox. Normalizing before
        // every comparison/store (both here and in UserRepository.ToEntity, implicitly, since this is
        // the only place emailaddress is ever set before being handed to the repository) means the DB
        // unique index and the proactive duplicate check actually agree with each other.
        private static string NormalizeEmail(string email) =>
            string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        public async Task<ProConnectResponse<SignUpResponse>> SignUp(SignUpRequest request)
        {
            var response = new ProConnectResponse<SignUpResponse>();
            var signUpResponse = new SignUpResponse();
            try
            {
                if (request is null)
                    return new ProConnectResponse<SignUpResponse>
                    {
                        issuccessfull = false,
                        errormessage = "Request body is required.",
                        message = "Request body is required."
                    };

                // BUG-01 fix: reject a duplicate signup for a sub that already has an account
                // (active or deactivated - GetBySubForAuthentication is unfiltered by is_active,
                // which is exactly what we want here: a deactivated user should sign back in via
                // LinkedInAuthentication, not create a second row) with a clean error instead of
                // letting it fall through to a raw Npgsql 23505 unique-violation 500.
                if (!string.IsNullOrEmpty(request.sub))
                {
                    var existingUser = await _userRepository.GetBySubForAuthentication(request.sub);
                    if (existingUser is not null)
                        return new ProConnectResponse<SignUpResponse>
                        {
                            issuccessfull = false,
                            errormessage = "A user with this account already exists. Please sign in instead.",
                            message = "A user with this account already exists. Please sign in instead."
                        };
                }

                // FetchLinkedInProfile caches the raw scrape result itself, so repeat signups (or a
                // GetLinkedInProfile lookup) for the same identifier don't hit the paid scraper API again
                var profileResponse = await FetchLinkedInProfile(request.public_identifier);

                // BUG-09 fix (follow-up discovered during reverification): a null profileResponse means
                // either the actor returned zero dataset items at all, or HarvestApiProfileProvider.GetProfile
                // itself rejected a mismatched/empty hit (IsPlausibleMatch). Both are the exact same
                // user-facing situation as the "empty core identity" case just below (which only fires when
                // profileResponse is non-null) - so this needs the same clean rejection response, not a thrown
                // InvalidOperationException. Throwing here used to escape to the generic catch below and
                // surface as a 500 with the raw exception text leaked into errormessage, which is exactly the
                // kind of information disclosure / unclean-error pattern BUG-09 was about in the first place.
                if (profileResponse is null)
                {
                    _logger.LogWarning(
                        "SignUp could not retrieve any LinkedIn profile for requested public_identifier '{Requested}'.",
                        request.public_identifier);

                    return new ProConnectResponse<SignUpResponse>
                    {
                        issuccessfull = false,
                        errormessage = "We couldn't retrieve a valid LinkedIn profile for this identifier. Please check it and try again.",
                        message = "We couldn't retrieve a valid LinkedIn profile for this identifier. Please check it and try again."
                    };
                }

                // BUG-09 fix (defense in depth): HarvestApiProfileProvider.GetProfile now rejects a
                // mismatched/empty scrape result itself (returning null, which is already handled above),
                // but this stays as a second line of defense against any IProfileProvider implementation -
                // today's or a future one - that doesn't do that filtering. A profile with no usable name
                // at all is never worth silently persisting as a "successful" signup - see BUGS_FOUND.md
                // BUG-09 (the "billgates"/"tobiasluetke"/"stewartbutterfield" empty-profile cases).
                if (string.IsNullOrWhiteSpace(profileResponse.first_name)
                    && string.IsNullOrWhiteSpace(profileResponse.last_name)
                    && string.IsNullOrWhiteSpace(profileResponse.full_name))
                {
                    _logger.LogWarning(
                        "SignUp rejected an empty/incomplete scraped profile for requested public_identifier '{Requested}' (returned public_identifier '{Returned}').",
                        request.public_identifier, profileResponse.public_identifier);

                    return new ProConnectResponse<SignUpResponse>
                    {
                        issuccessfull = false,
                        errormessage = "We couldn't retrieve a valid LinkedIn profile for this identifier. Please check it and try again.",
                        message = "We couldn't retrieve a valid LinkedIn profile for this identifier. Please check it and try again."
                    };
                }

                // Observability only (not a rejection) - HarvestApiProfileProvider already validated
                // relatedness between the requested identifier and what it returned before handing this
                // back, so a mismatch here would be surprising; logging it means a future regression in
                // that validation is caught quickly instead of silently persisting the wrong identity
                // again (the original "Andy Gu" case from BUG-09's discovery).
                if (!string.IsNullOrWhiteSpace(profileResponse.public_identifier)
                    && !string.IsNullOrWhiteSpace(request.public_identifier)
                    && !string.Equals(profileResponse.public_identifier, request.public_identifier, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "SignUp: scraped public_identifier '{Returned}' differs from requested '{Requested}'.",
                        profileResponse.public_identifier, request.public_identifier);
                }

                // set gender, date of birth and email address - applied on every call, not cached, so a
                // later signup for the same identifier with different request data isn't silently stuck
                // with whichever request happened to populate the cache first
                profileResponse.userid = Guid.NewGuid();
                profileResponse.emailaddress = NormalizeEmail(
                    string.IsNullOrEmpty(profileResponse.emailaddress) ? request.email : profileResponse.emailaddress);
                profileResponse.gender = string.IsNullOrEmpty(profileResponse.gender) ? request.gender : profileResponse.gender;
                profileResponse.birth_date = profileResponse.birth_date is null ?
                                           new date { day = request.birth_date.day, month = request.birth_date.month, year = request.birth_date.year } :
                                           profileResponse.birth_date;

                // BUG-14 fix (corrected): reject a duplicate signup for an email that already has an
                // account (active or deactivated - GetByEmailForAuthentication is unfiltered by
                // is_active, same rationale as the sub check above). This must run against
                // profileResponse.emailaddress - the normalized, *effective* email that is actually
                // about to be persisted and is what the DB's unique index enforces - not the raw
                // request.email, because HarvestApi's scrape can supply its own emailaddress
                // (HarvestApiProfileProvider.cs) which overrides request.email above and would
                // otherwise let a colliding scraped email sail past this check only to blow up on the
                // DB unique-constraint insert (or, conversely, wrongly reject a signup whose
                // request.email collided but whose effective scraped email was actually unique).
                if (!string.IsNullOrEmpty(profileResponse.emailaddress))
                {
                    var existingEmailUser = await _userRepository.GetByEmailForAuthentication(profileResponse.emailaddress);
                    if (existingEmailUser is not null)
                        return new ProConnectResponse<SignUpResponse>
                        {
                            issuccessfull = false,
                            errormessage = "A user with this email already exists. Please sign in instead.",
                            message = "A user with this email already exists. Please sign in instead."
                        };
                }

                // save to database
                // user
                var userrequest = new UserRequest
                {
                    userid = profileResponse.userid,
                    sub = request.sub,
                    emailaddress = profileResponse.emailaddress,
                    background_cover_image_url = profileResponse.background_cover_image_url,
                    city = profileResponse.city,
                    country = profileResponse.country,
                    country_full_name = profileResponse.country_full_name,
                    first_name = profileResponse.first_name,
                    follower_count = profileResponse.follower_count,
                    full_name = profileResponse.full_name,
                    headline = profileResponse.headline,
                    last_name = profileResponse.last_name,
                    occupation = profileResponse.occupation,
                    profile_pic_url = profileResponse.profile_pic_url,
                    state = profileResponse.state,
                    summary = profileResponse.summary,
                    public_identifier = profileResponse.public_identifier,
                    birth_date = profileResponse.birth_date,
                    gender = profileResponse.gender,
                    industry = profileResponse.industry,
                    interests = profileResponse.interests,
                    company = profileResponse.experiences?.FirstOrDefault()?.company
                };

                await PopulateCoordinates(userrequest);

                signUpResponse = await _userRepository.SignUp(userrequest);

                // experience
                // education
                // skills

                var (accesstoken, refreshtoken) = await IssueTokenPair(profileResponse.userid, profileResponse.public_identifier, profileResponse.emailaddress);

                // generate ViewModel
                signUpResponse = new SignUpResponse
                {
                    userid = profileResponse.userid,
                    background_cover_image_url = profileResponse.background_cover_image_url,
                    city = profileResponse.city,
                    country = profileResponse.country,
                    country_full_name = profileResponse.country_full_name,
                    first_name = profileResponse.first_name,
                    follower_count = profileResponse.follower_count,
                    full_name = profileResponse.full_name,
                    headline = profileResponse.headline,
                    last_name = profileResponse.last_name,
                    occupation = profileResponse.occupation,
                    profile_pic_url = profileResponse.profile_pic_url,
                    state = profileResponse.state,
                    summary = profileResponse.summary,
                    public_identifier = profileResponse.public_identifier,
                    birth_date = profileResponse.birth_date,
                    gender = profileResponse.gender,
                    token = accesstoken,
                    refreshtoken = refreshtoken
                };

                response = new ProConnectResponse<SignUpResponse>
                {
                    body = signUpResponse,
                    message = "User has successfully signed up."
                };

                // brand-new user is about to land on the swipe deck for the first time - see
                // TriggerRecommendationWarmUp.
                TriggerRecommendationWarmUp(_matchService, profileResponse.userid);
            }
            catch (DuplicateUserException dupEx)
            {
                // Defense-in-depth: the proactive sub/email checks above are check-then-act, so a true
                // concurrent-request race (two genuinely simultaneous signups for the same new sub/email)
                // can slip both requests past those checks before either one's insert has committed.
                // UserRepository.SignUp catches the resulting Postgres 23505 unique-violation and throws
                // this typed exception instead of letting a raw Npgsql exception escape as an unhandled
                // 500 - map it back here to the exact same clean message the proactive check would have
                // returned had the race not occurred.
                _logger.LogWarning(dupEx,
                    "SignUp hit a concurrent-signup race on '{ConflictField}' for public_identifier '{Requested}'.",
                    dupEx.ConflictField, request?.public_identifier);

                return dupEx.ConflictField == "email"
                    ? new ProConnectResponse<SignUpResponse>
                    {
                        issuccessfull = false,
                        errormessage = "A user with this email already exists. Please sign in instead.",
                        message = "A user with this email already exists. Please sign in instead."
                    }
                    : new ProConnectResponse<SignUpResponse>
                    {
                        issuccessfull = false,
                        errormessage = "A user with this account already exists. Please sign in instead.",
                        message = "A user with this account already exists. Please sign in instead."
                    };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SignUp failed for public_identifier '{request?.public_identifier}'.");
                throw;
            }

            return response;
        }
        public async Task<ProConnectResponse<PersonProfileResponse>> GetLinkedInProfile(string publicIdentifier)
        {
            try
            {
                if (string.IsNullOrEmpty(publicIdentifier))
                    return new ProConnectResponse<PersonProfileResponse>
                    {
                        issuccessfull = false,
                        errormessage = "public_identifier is required.",
                        message = "public_identifier is required."
                    };

                var profile = await FetchLinkedInProfile(publicIdentifier);

                if (profile is null)
                    return new ProConnectResponse<PersonProfileResponse>
                    {
                        issuccessfull = false,
                        errormessage = $"LinkedIn profile could not be found for '{publicIdentifier}'.",
                        message = $"LinkedIn profile could not be found for '{publicIdentifier}'."
                    };

                return new ProConnectResponse<PersonProfileResponse>
                {
                    body = profile,
                    message = "Profile has been fetched successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GetLinkedInProfile failed for public_identifier '{publicIdentifier}'.");
                throw;
            }
        }
        public async Task<ProConnectResponse<Content>> GenerateConsentLinkedInLogInPage()
        {
            try
            {

                var encoded_url = HttpUtility.UrlEncode(_proConnectConfig.linkedInApplicationConfig.redirect_url);
                var path = $"authorization?response_type=code&" +
                           $"state={Guid.NewGuid()}&" +
                           $"scope=profile,email,openid&" +
                           $"client_id={_proConnectConfig.linkedInApplicationConfig.client_id}&" +
                           $"redirect_uri={encoded_url}&" +
                           $"response_mode=form_post";
                var content = await _httpClientUtilities.GetStringAsync(_proConnectConfig.linkedInApplicationConfig.linkedin_baseurl, path);
                return new ProConnectResponse<Content>
                {
                    body = new Content { htmlcontent = content },
                    message = "Consent page has been successfully generated."
                };

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GenerateConsentLinkedInLogInPage failed.");
                throw;
            }
        }
        public async Task<ProConnectResponse<SignInResponse>> LinkedInAuthentication(string code, string state)
        {
            try
            {
                // call accessToken endpoint to generate token
                var tokenPath = $"{_proConnectConfig.linkedInApplicationConfig.linkedin_baseurl}{_proConnectConfig.linkedInApplicationConfig.linkedin_accesstokenpath}?" +
                    $"grant_type=authorization_code&" +
                    $"code={code}&" +
                    $"redirect_uri={HttpUtility.UrlEncode(_proConnectConfig.linkedInApplicationConfig.redirect_url)}&" +
                    $"client_id={_proConnectConfig.linkedInApplicationConfig.client_id}&" +
                    $"client_secret={_proConnectConfig.linkedInApplicationConfig.client_secret}";

                // BUG-02 fix: a genuinely bad/expired code or state makes LinkedIn's token
                // endpoint (or, less commonly, the userinfo endpoint once a bad/expired token
                // slips through) respond with a non-2xx HTTP status. IHttpClientUtilities throws
                // on that (PostAsync: a generic Exception; GetAsync->EnsureSuccessStatusCode: an
                // HttpRequestException) *before* either of the existing "200 OK but empty
                // token/sub" clean-error checks below ever runs. Wrapping just these two calls
                // turns that into the same clean "LinkedIn authentication failed" shape instead
                // of an unhandled 500, without swallowing unrelated exceptions from the rest of
                // this method.
                TokenResponse tokenResponse;
                SignInResponse userInfoResponse;
                try
                {
                    tokenResponse = await _httpClientUtilities.PostAsync<TokenResponse>(tokenPath, new object { });

                    if (tokenResponse is null || string.IsNullOrEmpty(tokenResponse.access_token))
                        return new ProConnectResponse<SignInResponse>
                        {
                            issuccessfull = false,
                            errormessage = "Failed to obtain an access token from LinkedIn.",
                            message = "Failed to obtain an access token from LinkedIn."
                        };

                    // call userinfo endpoint to pull user's lite profile
                    var userInfoPath = $"{_proConnectConfig.linkedInApplicationConfig.linkedin_api_baseurl}";
                    userInfoResponse = await _httpClientUtilities.GetAsync<SignInResponse>(userInfoPath, _proConnectConfig.linkedInApplicationConfig.linkedin_userinfopath, tokenResponse.access_token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "LinkedIn token exchange or userinfo call returned a non-2xx response (likely a bad/expired code or state).");
                    return new ProConnectResponse<SignInResponse>
                    {
                        issuccessfull = false,
                        errormessage = "LinkedIn authentication failed. Please try signing in again.",
                        message = "LinkedIn authentication failed. Please try signing in again."
                    };
                }

                if (userInfoResponse is null || string.IsNullOrEmpty(userInfoResponse.sub))
                    return new ProConnectResponse<SignInResponse>
                    {
                        issuccessfull = false,
                        errormessage = "The user is not exist in LinkedIn.",
                        message = "The user is not exist in LinkedIn."
                    };

                // look up the local account tied to this LinkedIn user so we can issue our own session token.
                // Uses GetBySubForAuthentication (not GetBySub) because a deactivated account must still be
                // found here - signing back in is what reactivates it (Instagram-style "welcome back"),
                // rather than requiring a separate dedicated reactivate endpoint.
                var existingUser = await _userRepository.GetBySubForAuthentication(userInfoResponse.sub);
                if (existingUser is null)
                    return new ProConnectResponse<SignInResponse>
                    {
                        issuccessfull = false,
                        errormessage = "The user has not signed up yet.",
                        message = "The user has not signed up yet."
                    };

                if (!existingUser.is_active)
                {
                    await _userRepository.Reactivate(existingUser.userid);
                    _logger.LogInformation($"Userid '{existingUser.userid}' signed back in and was reactivated (was previously deactivated).");
                }

                (userInfoResponse.token, userInfoResponse.refreshtoken) = await IssueTokenPair(existingUser.userid, existingUser.public_identifier, existingUser.emailaddress);

                // returning user is about to land on the swipe deck - see TriggerRecommendationWarmUp.
                TriggerRecommendationWarmUp(_matchService, existingUser.userid);

                var response = new ProConnectResponse<SignInResponse>
                {
                    body = userInfoResponse,
                    message = "User's info has been fetched successfully."
                };
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LinkedInAuthentication failed.");
                throw;
            }
        }
        public async Task<ProConnectResponse<SignInResponse>> SystemAuthentication(Guid userid)
        {
            try
            {
                var user = await _userRepository.GetById(userid);

                if (user is null)
                    return new ProConnectResponse<SignInResponse>
                    {
                        issuccessfull = false,
                        errormessage = "User not found.",
                        message = "User not found."
                    };

                // SystemAuthentication is the "/me" check the client calls on every app launch/token
                // refresh (see PROJECT_NOTES.md Phase 1) - the most direct "the user just opened the app"
                // signal available, so this is the primary warm-up trigger. See TriggerRecommendationWarmUp.
                TriggerRecommendationWarmUp(_matchService, userid);

                return new ProConnectResponse<SignInResponse>
                {
                    body = new SignInResponse
                    {
                        sub = user.sub,
                        email = user.emailaddress,
                        name = user.full_name,
                        given_name = user.first_name,
                        family_name = user.last_name,
                        picture = user.profile_pic_url
                    },
                    message = "User's info has been fetched successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SystemAuthentication failed for userid '{userid}'.");
                throw;
            }
        }
        public async Task<ProConnectResponse<SignUpResponse>> GetProfile(Guid userid)
        {
            try
            {
                var user = await _userRepository.GetById(userid);

                if (user is null)
                    return new ProConnectResponse<SignUpResponse>
                    {
                        issuccessfull = false,
                        errormessage = "User not found.",
                        message = "User not found."
                    };

                return new ProConnectResponse<SignUpResponse>
                {
                    body = MapToSignUpResponse(user),
                    message = "Profile has been fetched successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GetProfile failed for userid '{userid}'.");
                throw;
            }
        }
        public async Task<ProConnectResponse<SignUpResponse>> UpdateProfile(Guid userid, UpdateProfileRequest request)
        {
            try
            {
                if (request is null)
                    return new ProConnectResponse<SignUpResponse>
                    {
                        issuccessfull = false,
                        errormessage = "Request body is required.",
                        message = "Request body is required."
                    };

                var user = await _userRepository.GetById(userid);

                if (user is null)
                    return new ProConnectResponse<SignUpResponse>
                    {
                        issuccessfull = false,
                        errormessage = "User not found.",
                        message = "User not found."
                    };

                var locationChanged = (request.city != null && request.city != user.city)
                    || (request.state != null && request.state != user.state)
                    || (request.country != null && request.country != user.country);

                user.first_name = request.first_name ?? user.first_name;
                user.last_name = request.last_name ?? user.last_name;
                user.headline = request.headline ?? user.headline;
                user.summary = request.summary ?? user.summary;
                user.occupation = request.occupation ?? user.occupation;
                user.city = request.city ?? user.city;
                user.state = request.state ?? user.state;
                user.country = request.country ?? user.country;
                user.profile_pic_url = request.profile_pic_url ?? user.profile_pic_url;
                user.background_cover_image_url = request.background_cover_image_url ?? user.background_cover_image_url;

                // only re-geocode when location text actually changed - avoids an unnecessary Nominatim
                // round trip (even cached ones) on every profile edit
                if (locationChanged)
                    await PopulateCoordinates(user);

                var updatedUser = await _userRepository.Update(user);

                if (updatedUser is null)
                    return new ProConnectResponse<SignUpResponse>
                    {
                        issuccessfull = false,
                        errormessage = "Failed to update the profile.",
                        message = "Failed to update the profile."
                    };

                return new ProConnectResponse<SignUpResponse>
                {
                    body = MapToSignUpResponse(updatedUser),
                    message = "Profile has been updated successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"UpdateProfile failed for userid '{userid}'.");
                throw;
            }
        }

        /// <summary>
        /// Temporarily deactivates the caller's own account (is_active = false) - Instagram-style "take a
        /// break", NOT a delete. Nothing is removed: matches/swipes/profile data all stay intact, just
        /// invisible everywhere (GetById, GetBySub, match candidates, search - see
        /// IUserRepository.Deactivate) until the user signs back in via LinkedIn, which automatically
        /// reactivates them (see LinkedInAuthentication). Self-service only: there's no admin/role system
        /// yet to support deactivating someone else's account. For permanent deletion, see DeleteAccount.
        /// </summary>
        public async Task<ProConnectResponse<string>> DeactivateAccount(Guid userid)
        {
            try
            {
                var deactivated = await _userRepository.Deactivate(userid);

                if (!deactivated)
                    return new ProConnectResponse<string>
                    {
                        issuccessfull = false,
                        errormessage = "User not found.",
                        message = "User not found."
                    };

                // A deactivated account shouldn't be redeemable via a refresh token that was issued while
                // it was still active - without this, a client holding a live refresh token could keep
                // silently minting new access tokens for an account the user just took a break from.
                await _refreshTokenRepository.RevokeAllForUser(userid);

                return new ProConnectResponse<string>
                {
                    body = "Deactivated",
                    message = "Account has been deactivated. Sign back in at any time to reactivate it."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"DeactivateAccount failed for userid '{userid}'.");
                throw;
            }
        }

        /// <summary>
        /// Permanently and irreversibly deletes the caller's own account and every piece of data that
        /// references it (swipes, matches, match criteria - see IUserRepository.DeleteAccount). This is
        /// NOT the same as DeactivateAccount above - there is no way back from this, unlike deactivation.
        /// Self-service only, same limitation as DeactivateAccount.
        /// </summary>
        public async Task<ProConnectResponse<string>> DeleteAccount(Guid userid)
        {
            try
            {
                var deleted = await _userRepository.DeleteAccount(userid);

                if (!deleted)
                    return new ProConnectResponse<string>
                    {
                        issuccessfull = false,
                        errormessage = "User not found.",
                        message = "User not found."
                    };

                // Not calling RevokeAllForUser here - refresh_token's FK to users is OnDelete(Cascade)
                // (see RefreshTokenEntityConfiguration), so every refresh token row for this user is
                // already gone along with the user row itself.

                return new ProConnectResponse<string>
                {
                    body = "Deleted",
                    message = "Account and all associated data have been permanently deleted."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"DeleteAccount failed for userid '{userid}'.");
                throw;
            }
        }

        /// <summary>
        /// Redeems a refresh token for a brand-new access+refresh pair, rotating the presented token out
        /// (see IRefreshTokenRepository.Rotate / RefreshTokenEntity remarks) so it can never be redeemed a
        /// second time. An unknown, already-rotated, revoked, or expired token all produce the exact same
        /// generic failure below by design - distinguishing them in the response would help an attacker
        /// fingerprint a stolen/replayed token.
        /// </summary>
        public async Task<ProConnectResponse<RefreshTokenResponse>> RefreshToken(string refreshtoken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(refreshtoken))
                    return new ProConnectResponse<RefreshTokenResponse>
                    {
                        issuccessfull = false,
                        errormessage = "refreshtoken is required.",
                        message = "refreshtoken is required."
                    };

                var tokenhash = _tokenService.HashToken(refreshtoken);
                var activetoken = await _refreshTokenRepository.GetActiveByHash(tokenhash);

                if (activetoken is null)
                    return new ProConnectResponse<RefreshTokenResponse>
                    {
                        issuccessfull = false,
                        errormessage = "Refresh token is invalid or has expired. Please sign in again.",
                        message = "Refresh token is invalid or has expired. Please sign in again."
                    };

                var user = await _userRepository.GetById(activetoken.userid);
                if (user is null)
                {
                    // The account was deactivated/deleted since this token was issued - GetById is
                    // filtered by is_active (see its remarks), so this also catches a deactivated user,
                    // whose tokens DeactivateAccount already revoked anyway; this is just defense in depth
                    // against any gap between the two.
                    return new ProConnectResponse<RefreshTokenResponse>
                    {
                        issuccessfull = false,
                        errormessage = "Refresh token is invalid or has expired. Please sign in again.",
                        message = "Refresh token is invalid or has expired. Please sign in again."
                    };
                }

                var newrefreshtoken = _tokenService.GenerateRefreshToken();
                await _refreshTokenRepository.Rotate(
                    tokenhash,
                    _tokenService.HashToken(newrefreshtoken),
                    DateTime.UtcNow.AddDays(_proConnectConfig.jwtConfig.refreshtokenexpiryindays));

                var newaccesstoken = _tokenService.GenerateToken(user.userid, user.public_identifier, user.emailaddress);

                return new ProConnectResponse<RefreshTokenResponse>
                {
                    body = new RefreshTokenResponse { token = newaccesstoken, refreshtoken = newrefreshtoken },
                    message = "Token has been refreshed successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RefreshToken failed.");
                throw;
            }
        }

        /// <summary>
        /// Revokes the given refresh token outright so it can never be redeemed again - the actual logic
        /// UserController.Logout was missing (it used to be a bare return Ok()). The paired access token
        /// keeps working until its own short expiry, same as any stateless-JWT logout without a
        /// server-side access-token blocklist; this at least stops the client from silently extending the
        /// session past that via /refreshtoken.
        /// </summary>
        public async Task<ProConnectResponse<string>> Logout(string refreshtoken)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(refreshtoken))
                    await _refreshTokenRepository.Revoke(_tokenService.HashToken(refreshtoken));

                return new ProConnectResponse<string>
                {
                    body = "LoggedOut",
                    message = "Logged out successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Logout failed.");
                throw;
            }
        }


        private static SignUpResponse MapToSignUpResponse(UserRequest user)
        {
            return new SignUpResponse
            {
                userid = user.userid,
                public_identifier = user.public_identifier,
                profile_pic_url = user.profile_pic_url,
                background_cover_image_url = user.background_cover_image_url,
                first_name = user.first_name,
                last_name = user.last_name,
                full_name = user.full_name,
                follower_count = user.follower_count,
                occupation = user.occupation,
                headline = user.headline,
                summary = user.summary,
                country = user.country,
                country_full_name = user.country_full_name,
                city = user.city,
                state = user.state,
                gender = user.gender,
                birth_date = user.birth_date
            };
        }
        /// <summary>
        /// Internal helper shared by SignUp and GetLinkedInProfile - not part of IUserService since nothing
        /// outside this class needs it. Cache-aside, keyed by LinkedIn public identifier, so a profile
        /// scraped once (from either entry point) isn't paid for again on every subsequent lookup.
        /// Cache.Get only caches non-null results, so a not-found identifier isn't cached and will retry
        /// against the live API on the next call - acceptable, since we don't want to permanently cache a
        /// failure that might just be transient.
        /// </summary>
        private async Task<PersonProfileResponse> FetchLinkedInProfile(string publicIdentifier)
        {
            if (string.IsNullOrEmpty(publicIdentifier))
                return null;

            return await _cache.Get(publicIdentifier, () => _profileProvider.GetProfile(publicIdentifier), CacheRegion.PERSONPROFILE, TimeSpan.FromSeconds(_cacheExpiryConfig.personprofile));
        }

        /// <summary>
        /// Resolves user.city/state/country into user.latitude/user.longitude via IGeocodingProvider
        /// (Nominatim) so radius-based match/search queries have a coordinate to work with. LinkedIn/harvestapi
        /// never supplies lat/long directly. Best-effort: geocoding failures (bad/ambiguous location text,
        /// Nominatim being unreachable, etc.) are logged and swallowed rather than propagated, since a signup
        /// or profile update shouldn't fail just because the free geocoder had a bad day - the user simply
        /// won't show up in radius searches until a later update re-triggers this.
        /// </summary>
        private async Task PopulateCoordinates(UserRequest user)
        {
            try
            {
                var coordinate = await _geocodingProvider.Geocode(user.city, user.state, user.country);
                if (coordinate is null)
                {
                    _logger.LogDebug($"Could not resolve coordinates for user {user.userid} (city={user.city}, state={user.state}, country={user.country})");
                    return;
                }

                user.latitude = coordinate.latitude;
                user.longitude = coordinate.longitude;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Geocoding failed for user {user.userid} (city={user.city}, state={user.state}, country={user.country})");
            }
        }
    }
}
