using Microsoft.Extensions.Logging;
using ProConnect.Application.Interface;
using ProConnect.Domain.Config;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using ProConnect.Infrastructure.Interface;
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

        public UserService(IUserRepository userRepository,
                           IHttpClientUtilities httpClientUtilities,
                           ILogger<UserService> logger)
        {
            _userRepository = userRepository;
            _httpClientUtilities = httpClientUtilities;
            _logger = logger;
        }
        public async Task<ProConnectResponse<SignUpResponse>> SignUp(SignUpRequest request, ProxyCurlConfig proxyCurlConfig)
        {
            var response = new ProConnectResponse<SignUpResponse>();
            try
            {
                // check in db , if the user is NOT exist

                // call proxycurl to fetch person linkedIn info
                //var person = await GetPersonLinkedInProfile(request.username, proxyCurlConfig);

                // save to database, redis
                await _userRepository.SignUp(request);
            }
            catch (Exception)
            {
                throw;
            }

            return response;
        }
        public async Task<PersonResponse> GetPersonLinkedInProfile(string username, ProxyCurlConfig proxyCurlConfig)
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
            var personInfo = await _httpClientUtilities.GetAsync<PersonResponse>(proxyCurlConfig.baseurl, path, proxyCurlConfig.authorizationtoken);

            return personInfo;
        }
    }
}
