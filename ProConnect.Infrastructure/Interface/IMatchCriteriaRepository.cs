using ProConnect.Domain.Model;
using System;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Interface
{
    public interface IMatchCriteriaRepository
    {
        Task<MatchCriteria> GetByUserId(Guid userid);
        Task<MatchCriteria> Upsert(MatchCriteria criteria);
    }
}
