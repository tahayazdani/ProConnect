namespace ProConnect.Domain.Request
{
    /// <summary>
    /// Shared pagination request for the "browse candidates" endpoints - GetRecommendations,
    /// GetIncomingLikes, and GetSeenProfiles. For GetRecommendations specifically, pages are served from a
    /// bounded, incrementally-grown cache window (see MatchRecommendationBatch /
    /// MatchService.RecommendationCacheKey) rather than the whole matching pool, so most page requests are a
    /// pure in-memory slice and only occasionally trigger a DB fetch to grow the window.
    /// </summary>
    public class MatchCandidatePageRequest
    {
        public MatchCandidatePageRequest()
        {

        }
        public int page { get; set; } = 1;
        public int pagesize { get; set; } = 50;

        /// <summary>
        /// Bypasses (and repopulates) the cached recommendation list before paging. Intended for the client
        /// to set once it has paged through everything currently cached (has_more = false on the last page)
        /// and wants a fresh recompute right away instead of waiting out the CacheExpiryConfig.
        /// matchrecommendation TTL (15 min by default). Has no effect on GetIncomingLikes, which was never cached to
        /// begin with (see MatchService.GetIncomingLikes).
        /// </summary>
        public bool refresh { get; set; } = false;
    }
}
