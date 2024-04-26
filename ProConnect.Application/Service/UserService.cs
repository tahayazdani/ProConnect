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
    public class UserService : BaseService, IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IHttpClientUtilities _httpClientUtilities;
        private readonly ILogger<UserService> _logger;
        private readonly Cache _cache;

        public UserService(IUserRepository userRepository,
                           IHttpClientUtilities httpClientUtilities,
                           ILogger<UserService> logger,
                           Cache cache)
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
                    personProfile.emailaddress = string.IsNullOrEmpty(personProfile.emailaddress) ? request.emailaddress : personProfile.emailaddress;
                    personProfile.gender = string.IsNullOrEmpty(personProfile.gender) ? request.gender : personProfile.gender;
                    personProfile.birth_date = personProfile.birth_date is null ?
                                               new date { day = request.birth_date.day, month = request.birth_date.month, year = request.birth_date.year } :
                                               personProfile.birth_date;

                    return personProfile;
                }, CacheRegion.PERSONPROFILE);

                // save to database
                signUpResponse = await _userRepository.SignUp(profileResponse);

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
    }
}
