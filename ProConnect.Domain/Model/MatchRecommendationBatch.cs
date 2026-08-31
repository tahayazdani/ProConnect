using ProConnect.Domain.Response;
using System.Collections.Generic;

namespace ProConnect.Domain.Model
{
    /// <summary>
    /// Cached per user under MatchService.RecommendationCacheKey - a bounded, incrementally-grown window
    /// into the DB-level filtered/ordered candidate query (MatchRepository.GetCandidates), not the whole
    /// candidate pool. Replaces the old "cache the entire scored pool" strategy, which scaled with total
    /// active-user count instead of staying flat - see PROJECT_NOTES.md.
    /// </summary>
    public class MatchRecommendationBatch
    {
        public MatchRecommendationBatch()
        {

        }

        public List<MatchCandidateResponse> Items { get; set; } = new();

        /// <summary>
        /// Where the next MatchRepository.GetCandidates call resumes from - advances by however many rows
        /// the previous fetch actually returned, so growing the batch never skips or repeats a candidate.
        /// </summary>
        public int NextOffset { get; set; }

        /// <summary>
        /// Set once a fetch returns fewer rows than requested - the user has genuinely seen everyone who
        /// currently matches their criteria, so later calls stop hitting the DB for more (until the cache
        /// entry itself expires/is invalidated by a criteria change) instead of re-querying for nothing.
        /// </summary>
        public bool ExhaustedPool { get; set; }

        /// <summary>
        /// Total candidates currently matching criteria (MatchRepository.CountCandidates), refreshed each
        /// time the batch grows - used for MatchCandidatePageResponse.total_count/has_more without loading
        /// every matching row just to count it.
        /// </summary>
        public int TotalCount { get; set; }
    }
}
