using ProConnect.Domain.Const;
using ProConnect.Domain.Request;
using ProConnect.Infrastructure.Interface;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Repository
{
    public class MatchRepository : IMatchRepository
    {
        public async Task<IEnumerable<UserRequest>> GetCandidates(Guid userid)
        {
            throw new NotImplementedException();
        }

        public async Task RecordSwipe(Guid userid, Guid targetUserId, SwipeAction action)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> HasLiked(Guid likerUserId, Guid targetUserId)
        {
            throw new NotImplementedException();
        }

        public async Task<Guid> CreateMatch(Guid userIdA, Guid userIdB)
        {
            throw new NotImplementedException();
        }
    }
}
