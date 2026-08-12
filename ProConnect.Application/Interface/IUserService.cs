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
        Task<ProConnectResponse<SignUpResponse>> SignUp(SignUpRequest request);
        Task<PersonProfileResponse> GetPersonLinkedInProfile(string username);
        Task<ProConnectResponse<Content>> GenerateConsentLinkedInLogInPage();
        Task<ProConnectResponse<SignInResponse>> LinkedInAuthentication(string code, string state);
        Task<ProConnectResponse<SignInResponse>> SystemAuthentication(Guid userid);
        Task<ProConnectResponse<SignUpResponse>> GetProfile(Guid userid);
        Task<ProConnectResponse<SignUpResponse>> UpdateProfile(Guid userid, UpdateProfileRequest request);
    }
}
