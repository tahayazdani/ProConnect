using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProConnect.Application.Interface
{
    public interface IMatchService
    {
        Task<ProConnectResponse<MatchCriteriaResponse>> GetCriteria(Guid userid);
        Task<ProConnectResponse<MatchCriteriaResponse>> UpdateCriteria(Guid userid, MatchCriteriaRequest request);
        Task<ProConnectResponse<List<MatchCandidateResponse>>> GetRecommendations(Guid userid);
        Task<ProConnectResponse<SwipeResponse>> Like(Guid userid, Guid targetUserId);
        Task<ProConnectResponse<SwipeResponse>> Pass(Guid userid, Guid targetUserId);
    }
}
