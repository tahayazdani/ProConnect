using System;

namespace ProConnect.Infrastructure.Persistence.Entity
{
    /// <summary>
    /// Created once both sides have liked each other (see MatchService.Swipe / MatchRepository.CreateMatch).
    /// userid_a/userid_b are unordered - CreateMatch checks both orderings before inserting, so a given
    /// pair only ever gets one row regardless of who triggered the reciprocal like.
    /// </summary>
    public class MatchEntity
    {
        public Guid matchid { get; set; }
        public Guid userid_a { get; set; }
        public Guid userid_b { get; set; }
        public DateTime matched_at_utc { get; set; }
    }
}
