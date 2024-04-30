using FluentValidation;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<UserService> _logger;
        private readonly Cache _cache;

        public UserService(IUserRepository userRepository,
                           IHttpClientUtilities httpClientUtilities,
                           ILogger<UserService> logger,
                           Cache cache
                           )
        {
            _userRepository = userRepository;
            _httpClientUtilities = httpClientUtilities;
            _logger = logger;
            _cache = cache;
        }
        public async Task<ProConnectResponse<SignUpResponse>> SignUp(SignUpRequest request, ProxyCurlConfig proxyCurlConfig)
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
                    var personProfile = await GetPersonLinkedInProfile(request.public_identifier, proxyCurlConfig);

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
        public async Task<PersonProfileResponse> GetPersonLinkedInProfile(string username, ProxyCurlConfig proxyCurlConfig)
        {
            // call proxycurl endpoint to pull person info, then register into system
            // params : skills : included , extra : include , _live_scrape : force (no caching)

            var path = $"{username}";
            if (!string.IsNullOrEmpty(proxyCurlConfig.extra))
                path += $"&extra={proxyCurlConfig.extra}";

            if (!string.IsNullOrEmpty(proxyCurlConfig.live_scrape))
                path += $"&_live_scrape={proxyCurlConfig.live_scrape}";

            if (!string.IsNullOrEmpty(proxyCurlConfig.skills))
                path += $"&skills={proxyCurlConfig.skills}";

            // fetch profile form proxycurl
            var personInfo = await _httpClientUtilities.GetAsync<PersonProfileResponse>(proxyCurlConfig.baseurl, path, proxyCurlConfig.authorizationtoken);

            return personInfo;
        }
        public async Task<ProConnectResponse<Content>> GenerateConsentLinkedInLogInPage(LinkedInApplicationConfig linkedInApplicationConfig)
        {
            try
            {

                var encoded_url = HttpUtility.UrlEncode(linkedInApplicationConfig.redirect_url);
                var path = $"authorization?response_type=code&" +
                           $"state={Guid.NewGuid()}&" +
                           $"scope=profile,email,openid&" +
                           $"client_id={linkedInApplicationConfig.client_id}&" +
                           $"redirect_uri={encoded_url}&" +
                           $"response_mode=form_post";
                var content = await _httpClientUtilities.GetStringAsync(linkedInApplicationConfig.linkedin_baseurl, path);
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
        public async Task<ProConnectResponse<SignInResponse>> Authenticate(string code, string state, LinkedInApplicationConfig linkedInApplicationConfig)
        {
            try
            {
                // call accessToken endpoint to generate token
                var tokenPath = $"{linkedInApplicationConfig.linkedin_baseurl}/accessToken?" +
                    $"grant_type=authorization_code&" +
                    $"code={code}&" +
                    $"redirect_uri={HttpUtility.UrlEncode(linkedInApplicationConfig.redirect_url)}&" +
                    $"client_id={linkedInApplicationConfig.client_id}&" +
                    $"client_secret={linkedInApplicationConfig.client_secret}";

                var tokenResponse = await _httpClientUtilities.PostAsync<TokenResponse>(tokenPath, new object { });


                // call userinfo endpoint to pull user's lite profile
                var userInfoPath = $"{linkedInApplicationConfig.linkedin_api_baseurl}";
                var userInfoResponse = await _httpClientUtilities.GetAsync<SignInResponse>(userInfoPath, "/userinfo", tokenResponse.access_token);

                if (string.IsNullOrEmpty(userInfoResponse.sub))
                    return new ProConnectResponse<SignInResponse>
                    {
                        issuccessfull = false,
                        errormessage = "The user is not exist in LinkedIn.",
                        message = "The user is not exist in LinkedIn."
                    };


                // check user's email address in database see if the user is exist or not (registred)
                // if the user is NOT exist then return a message
                // if the user is exist but data expired then just call GetPersonLinkedInProfile endpoint to update db as well as redis
                // if the user exist and data is NOT expired, then all good.


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
    }
}
