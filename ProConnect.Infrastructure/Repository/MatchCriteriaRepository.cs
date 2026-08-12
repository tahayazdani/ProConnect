using ProConnect.Domain.Model;
using ProConnect.Infrastructure.Interface;
using System;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Repository
{
    public class MatchCriteriaRepository : IMatchCriteriaRepository
    {
        public async Task<MatchCriteria> GetByUserId(Guid userid)
        {
            throw new NotImplementedException();
        }

        public async Task<MatchCriteria> Upsert(MatchCriteria criteria)
        {
            throw new NotImplementedException();
        }
    }
}
