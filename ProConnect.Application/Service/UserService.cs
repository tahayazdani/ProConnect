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

        public UserService(IUserRepository userRepository,
                           IHttpClientUtilities httpClientUtilities)
        {
            _userRepository = userRepository;
            _httpClientUtilities = httpClientUtilities;
        }
        public async Task<ProConnectResponse<SignUpResponse>> SignUp(SignUpRequest request, ProxyCurlConfig proxyCurlConfig)
        {
            var response = new ProConnectResponse<SignUpResponse>();

            try
            {
                // check in db , if the user is NOT exist

                // call proxycurl to fetch person linkedIn info
                var person = await GetPersonLinkedInInfo(request, proxyCurlConfig);

                // save to database
                await _userRepository.SignUp(request);
            }
            catch (Exception)
            {
                throw;
            }

            return response;
        }
        private async Task<PersonResponse> GetPersonLinkedInInfo(SignUpRequest request, ProxyCurlConfig proxyCurlConfig)
        {
            // call proxycurl endpoint to pull person info, then register into system
            // params : skills : included , extra : include , _live_scrape : force (no caching)

            var path = $"{request.username}&extra={proxyCurlConfig.extra}&_live_scrape={proxyCurlConfig.live_scrape}&skills={proxyCurlConfig.skills}";
            var personInfo = await _httpClientUtilities.GetAsync<PersonResponse>(proxyCurlConfig.baseurl, path, proxyCurlConfig.authorizationtoken);

            return personInfo;
        }
    }
}
