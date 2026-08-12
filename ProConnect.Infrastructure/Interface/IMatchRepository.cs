using ProConnect.Domain.Const;
using ProConnect.Domain.Request;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Interface
{
    public interface IMatchRepository
    {
        /// <summary>
        /// Candidate pool for recommendations - excludes the given user and anyone they've already swiped on.
        /// </summary>
        Task<IEnumerable<UserRequest>> GetCandidates(Guid userid);
        Task RecordSwipe(Guid userid, Guid targetUserId, SwipeAction action);
        /// <summary>
        /// Whether `likerUserId` has previously liked `targetUserId`.
        /// </summary>
        Task<bool> HasLiked(Guid likerUserId, Guid targetUserId);
        Task<Guid> CreateMatch(Guid userIdA, Guid userIdB);
    }
}
