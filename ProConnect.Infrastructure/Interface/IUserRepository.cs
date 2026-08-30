using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Interface
{
    public interface IUserRepository
    {
        Task<SignUpResponse> SignUp(UserRequest request);
        Task<UserRequest> GetById(Guid userid);
        Task<UserRequest> GetBySub(string sub);
        /// <summary>
        /// Same lookup as GetBySub, but includes deactivated accounts - the one legitimate place a
        /// deactivated user needs to be found instead of treated as nonexistent. Used exclusively by
        /// UserService.LinkedInAuthentication so signing back in can detect and reactivate a deactivated
        /// account (see Reactivate). Every other consumer should keep using GetBySub.
        /// </summary>
        Task<UserRequest> GetBySubForAuthentication(string sub);
        Task<UserRequest> Update(UserRequest request);
        /// <summary>
        /// Temporary, reversible deactivation (is_active = false) - Instagram-style "take a break", NOT a
        /// delete. Preserves matches/swipes/history and makes the user invisible everywhere else (GetById,
        /// GetBySub, match candidates, search) until they sign back in, at which point
        /// UserService.LinkedInAuthentication calls Reactivate automatically. Returns false if the userid
        /// doesn't exist at all. For permanent removal of the account and its data, see DeleteAccount.
        /// </summary>
        Task<bool> Deactivate(Guid userid);
        /// <summary>
        /// Flips is_active back to true - the reactivation half of Deactivate. Called automatically by
        /// UserService.LinkedInAuthentication when a deactivated user signs back in; there is no separate
        /// user-facing "reactivate" endpoint by design (signing in IS the reactivation action, same as
        /// Instagram). Returns false if the userid doesn't exist at all.
        /// </summary>
        Task<bool> Reactivate(Guid userid);
        /// <summary>
        /// Permanent, irreversible hard delete of the user row AND every row that references it (swipes in
        /// either direction, matches on either side, match criteria) - NOT the same thing as Deactivate.
        /// This is for a user who explicitly wants their data gone, not a temporary break. Returns false if
        /// the userid doesn't exist at all.
        /// </summary>
        Task<bool> DeleteAccount(Guid userid);
    }
}
