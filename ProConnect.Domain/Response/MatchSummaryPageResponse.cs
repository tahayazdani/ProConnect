using System.Collections.Generic;

namespace ProConnect.Domain.Response
{
    /// <summary>
    /// Paged wrapper around MatchSummaryResponse - GetMatches used to return every match in one shot
    /// (assumed a small-enough pool); paged at the DB level (see IMatchRepository.GetMatches) so a
    /// long-tenured/highly-active user's full match history isn't materialized just to serve one page.
    /// Same shape/semantics as MatchCandidatePageResponse.
    /// </summary>
    public class MatchSummaryPageResponse
    {
        public MatchSummaryPageResponse()
        {

        }
        public List<MatchSummaryResponse> results { get; set; } = new();
        public int total_count { get; set; }
        public int page { get; set; }
        public int pagesize { get; set; }
        public bool has_more { get; set; }
    }
}
