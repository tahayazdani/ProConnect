using ProConnect.Domain.Config;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Application.Interface
{
    public interface IUserService
    {
        Task<ProConnectResponse<SignUpResponse>> SignUp(SignUpRequest request, ProxyCurlConfig proxyCurlConfig);
        Task<PersonProfileResponse> GetPersonLinkedInProfile(string username, ProxyCurlConfig proxyCurlConfig);
        Task<ProConnectResponse<Content>> GenerateConsentLinkedInLogInPage(LinkedInApplicationConfig linkedInApplicationConfig);
        Task<ProConnectResponse<SignInResponse>> Authenticate(string code, string state, LinkedInApplicationConfig linkedInApplicationConfig);
    }
}
