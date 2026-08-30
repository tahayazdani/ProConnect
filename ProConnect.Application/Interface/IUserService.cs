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
        Task<ProConnectResponse<PersonProfileResponse>> GetLinkedInProfile(string publicIdentifier);
        Task<ProConnectResponse<Content>> GenerateConsentLinkedInLogInPage();
        Task<ProConnectResponse<SignInResponse>> LinkedInAuthentication(string code, string state);
        Task<ProConnectResponse<SignInResponse>> SystemAuthentication(Guid userid);
        Task<ProConnectResponse<SignUpResponse>> GetProfile(Guid userid);
        Task<ProConnectResponse<SignUpResponse>> UpdateProfile(Guid userid, UpdateProfileRequest request);
        /// <summary>
        /// Temporarily deactivates the caller's own account - reversible, nothing is deleted. Reactivated
        /// automatically the next time the user signs back in (see LinkedInAuthentication). Self-service
        /// only for now - there's no admin/role system in this codebase yet. For permanent deletion, see
        /// DeleteAccount.
        /// </summary>
        Task<ProConnectResponse<string>> DeactivateAccount(Guid userid);
        /// <summary>
        /// Permanently and irreversibly deletes the caller's own account and all associated data. NOT the
        /// same as DeactivateAccount - there is no way back from this. Self-service only, same limitation
        /// as DeactivateAccount.
        /// </summary>
        Task<ProConnectResponse<string>> DeleteAccount(Guid userid);
    }
}
