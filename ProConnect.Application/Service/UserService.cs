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
        private readonly ProConnectConfig _proConnectConfig;
        private readonly ILogger<UserService> _logger;
        private readonly Cache _cache;

        public UserService(IUserRepository userRepository,
                           IHttpClientUtilities httpClientUtilities,
                           ILogger<UserService> logger,
                           Cache cache,
                           IOptions<ProConnectConfig> proConnectConfig
                           )
        {
            _userRepository = userRepository;
            _httpClientUtilities = httpClientUtilities;
            _logger = logger;
            _cache = cache;
            _proConnectConfig = proConnectConfig.Value;
        }
        public async Task<ProConnectResponse<SignUpResponse>> SignUp(SignUpRequest request)
        {
            var response = new ProConnectResponse<SignUpResponse>();
            var signUpResponse = new SignUpResponse();
            try
            {
                // check in db , if the user is NOT exist


                // call proxycurl to fetch person linkedIn info
                var cachekey = request.public_identifier;
                var profileResponse = await _cache.Get(cachekey, async () =>
                {
                    var personProfile = await GetPersonLinkedInProfile(request.public_identifier);

                    // set gender, date of birth and email address
                    personProfile.userid = Guid.NewGuid();
                    personProfile.emailaddress = string.IsNullOrEmpty(personProfile.emailaddress) ? request.email : personProfile.emailaddress;
                    personProfile.gender = string.IsNullOrEmpty(personProfile.gender) ? request.gender : personProfile.gender;
                    personProfile.birth_date = personProfile.birth_date is null ?
                                               new date { day = request.birth_date.day, month = request.birth_date.month, year = request.birth_date.year } :
                                               personProfile.birth_date;

                    return personProfile;
                }, CacheRegion.PERSONPROFILE);

                // save to database
                // user
                var userrequest = new UserRequest
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
                    gender = profileResponse.gender
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
                    gender = profileResponse.gender
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
        public async Task<PersonProfileResponse> GetPersonLinkedInProfile(string username)
        {
            // call proxycurl endpoint to pull person info, then register into system
            // params : skills : included , extra : include , _live_scrape : force (no caching)

            var path = $"{username}";
            if (!string.IsNullOrEmpty(_proConnectConfig.proxyCurlConfig.extra))
                path += $"&extra={_proConnectConfig.proxyCurlConfig.extra}";

            if (!string.IsNullOrEmpty(_proConnectConfig.proxyCurlConfig.live_scrape))
                path += $"&_live_scrape={_proConnectConfig.proxyCurlConfig.live_scrape}";

            if (!string.IsNullOrEmpty(_proConnectConfig.proxyCurlConfig.skills))
                path += $"&skills={_proConnectConfig.proxyCurlConfig.skills}";

            if (!string.IsNullOrEmpty(_proConnectConfig.proxyCurlConfig.use_cache))
                path += $"&use_cache={_proConnectConfig.proxyCurlConfig.use_cache}";

            if (!string.IsNullOrEmpty(_proConnectConfig.proxyCurlConfig.fallback_to_cache))
                path += $"&fallback_to_cache={_proConnectConfig.proxyCurlConfig.fallback_to_cache}";

            if (!string.IsNullOrEmpty(_proConnectConfig.proxyCurlConfig.personal_email))
                path += $"&personal_email={_proConnectConfig.proxyCurlConfig.personal_email}";

            // fetch profile form proxycurl
            var personInfo = await _httpClientUtilities.GetAsync<PersonProfileResponse>(_proConnectConfig.proxyCurlConfig.baseurl, path, _proConnectConfig.proxyCurlConfig.authorizationtoken);

            return personInfo;
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


                // call userinfo endpoint to pull user's lite profile
                var userInfoPath = $"{_proConnectConfig.linkedInApplicationConfig.linkedin_api_baseurl}";
                var userInfoResponse = await _httpClientUtilities.GetAsync<SignInResponse>(userInfoPath, _proConnectConfig.linkedInApplicationConfig.linkedin_userinfopath, tokenResponse.access_token);

                if (string.IsNullOrEmpty(userInfoResponse.sub))
                    return new ProConnectResponse<SignInResponse>
                    {
                        issuccessfull = false,
                        errormessage = "The user is not exist in LinkedIn.",
                        message = "The user is not exist in LinkedIn."
                    };

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
        public async Task<ProConnectResponse<SignInResponse>> SystemAuthentication()
        {
            try
            {
                return new ProConnectResponse<SignInResponse> { };
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
