using System.Collections.Generic;

namespace ProConnect.Domain.Response
{
    /// <summary>
    /// Paged wrapper around MatchCandidateResponse, shared by GetRecommendations and GetIncomingLikes.
    /// has_more tells the client whether to prefetch the next page (or, once false, whether to send
    /// MatchCandidatePageRequest.refresh = true on the next call to force a recompute) rather than assuming
    /// there's more just because the page came back full.
    /// </summary>
    public class MatchCandidatePageResponse
    {
        public MatchCandidatePageResponse()
        {

        }
        public List<MatchCandidateResponse> results { get; set; } = new();
        public int total_count { get; set; }
        public int page { get; set; }
        public int pagesize { get; set; }
        public bool has_more { get; set; }
    }
}
