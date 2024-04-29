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
            var encoded_url = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(linkedInApplicationConfig.redirect_url));

            var path = $"authorization?response_type=code&" +
                       $"state={Guid.NewGuid()}&" +
                       $"scope=email,openid,profile,w_member_social&" +
                       $"client_id={linkedInApplicationConfig.client_id}&" +
                       $"redirect_uri={encoded_url}&" +
                       $"response_mode=form_post";
            var content = await _httpClientUtilities.GetStringAsync(linkedInApplicationConfig.linkedin_baseurl, path);
            return new ProConnectResponse<Content> { body = new Content { htmlcontent = content } };
        }
    }
}
