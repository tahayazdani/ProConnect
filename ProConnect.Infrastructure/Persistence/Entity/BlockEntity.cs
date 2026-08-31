using System;

namespace ProConnect.Infrastructure.Persistence.Entity
{
    /// <summary>
    /// One row per directional block (blocker_user_id blocked blocked_user_id). Visibility, however, is
    /// enforced as mutual: MatchRepository.GetCandidates excludes a pair from each other's recommendations
    /// regardless of which side placed the block (see MatchRepository.BlockUser / GetCandidates). Blocking
    /// also tears down any existing match between the pair - see MatchRepository.BlockUser.
    /// </summary>
    public class BlockEntity
    {
        public Guid blocker_user_id { get; set; }
        public Guid blocked_user_id { get; set; }
        public DateTime blocked_at_utc { get; set; }
    }
}
