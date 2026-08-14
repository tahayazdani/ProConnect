using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProConnect.Application.Interface;
using ProConnect.Domain.Config;
using ProConnect.Domain.Const;
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
        private readonly ProConnectConfig _proConnectConfig;
        private readonly ILogger<UserService> _logger;
        private readonly Cache _cache;
        private readonly ITokenService _tokenService;

        public UserService(IUserRepository userRepository,
                           IHttpClientUtilities httpClientUtilities,
                           IProfileProvider profileProvider,
                           ILogger<UserService> logger,
                           Cache cache,
                           ITokenService tokenService,
                           IOptions<ProConnectConfig> proConnectConfig
                           )
        {
            _userRepository = userRepository;
            _httpClientUtilities = httpClientUtilities;
            _profileProvider = profileProvider;
            _logger = logger;
            _cache = cache;
            _tokenService = tokenService;
            _proConnectConfig = proConnectConfig.Value;
        }
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

                // check in db , if the user is NOT exist


                // FetchLinkedInProfile caches the raw scrape result itself, so repeat signups (or a
                // GetLinkedInProfile lookup) for the same identifier don't hit the paid scraper API again
                var profileResponse = await FetchLinkedInProfile(request.public_identifier);

                if (profileResponse is null)
                    throw new InvalidOperationException($"LinkedIn profile could not be retrieved for '{request.public_identifier}'.");

                // set gender, date of birth and email address - applied on every call, not cached, so a
                // later signup for the same identifier with different request data isn't silently stuck
                // with whichever request happened to populate the cache first
                profileResponse.userid = Guid.NewGuid();
                profileResponse.emailaddress = string.IsNullOrEmpty(profileResponse.emailaddress) ? request.email : profileResponse.emailaddress;
                profileResponse.gender = string.IsNullOrEmpty(profileResponse.gender) ? request.gender : profileResponse.gender;
                profileResponse.birth_date = profileResponse.birth_date is null ?
                                           new date { day = request.birth_date.day, month = request.birth_date.month, year = request.birth_date.year } :
                                           profileResponse.birth_date;

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
                signUpResponse = await _userRepository.SignUp(userrequest);

                // experience
                // education
                // skills

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
                    token = _tokenService.GenerateToken(profileResponse.userid, profileResponse.public_identifier, profileResponse.emailaddress)
                };

                response = new ProConnectResponse<SignUpResponse>
                {
                    body = signUpResponse,
                    message = "User has successfully signed up."
                };
            }
            catch (Exception)
            {
                throw;
            }

            return response;
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

            return await _cache.Get(publicIdentifier, () => _profileProvider.GetProfile(publicIdentifier), CacheRegion.PERSONPROFILE, CacheExpiry.PersonProfile);
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
            catch (Exception)
            {
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
            catch (Exception)
            {
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

                var tokenResponse = await _httpClientUtilities.PostAsync<TokenResponse>(tokenPath, new object { });

                if (tokenResponse is null || string.IsNullOrEmpty(tokenResponse.access_token))
                    return new ProConnectResponse<SignInResponse>
                    {
                        issuccessfull = false,
                        errormessage = "Failed to obtain an access token from LinkedIn.",
                        message = "Failed to obtain an access token from LinkedIn."
                    };

                // call userinfo endpoint to pull user's lite profile
                var userInfoPath = $"{_proConnectConfig.linkedInApplicationConfig.linkedin_api_baseurl}";
                var userInfoResponse = await _httpClientUtilities.GetAsync<SignInResponse>(userInfoPath, _proConnectConfig.linkedInApplicationConfig.linkedin_userinfopath, tokenResponse.access_token);

                if (userInfoResponse is null || string.IsNullOrEmpty(userInfoResponse.sub))
                    return new ProConnectResponse<SignInResponse>
                    {
                        issuccessfull = false,
                        errormessage = "The user is not exist in LinkedIn.",
                        message = "The user is not exist in LinkedIn."
                    };

                // look up the local account tied to this LinkedIn user so we can issue our own session token
                var existingUser = await _userRepository.GetBySub(userInfoResponse.sub);
                if (existingUser is null)
                    return new ProConnectResponse<SignInResponse>
                    {
                        issuccessfull = false,
                        errormessage = "The user has not signed up yet.",
                        message = "The user has not signed up yet."
                    };

                userInfoResponse.token = _tokenService.GenerateToken(existingUser.userid, existingUser.public_identifier, existingUser.emailaddress);

                var response = new ProConnectResponse<SignInResponse>
                {
                    body = userInfoResponse,
                    message = "User's info has been fetched successfully."
                };
                return response;
            }
            catch (Exception)
            {
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
            catch (Exception)
            {
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
            catch (Exception)
            {
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
            catch (Exception)
            {
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
    }
}
