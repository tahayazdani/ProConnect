using ProConnect.Domain.Const;
using System;

namespace ProConnect.Infrastructure.Persistence.Entity
{
    /// <summary>
    /// One row per (userid, targetuserid) pair - composite primary key, so RecordSwipe is a natural upsert
    /// (a user changing their mind from Pass to Like just overwrites the row) rather than an ever-growing
    /// history table. MatchRepository.GetCandidates excludes any targetuserid already present here for the
    /// given userid; HasLiked/CreateMatch check for a reciprocal Like row.
    /// </summary>
    public class SwipeEntity
    {
        public Guid userid { get; set; }
        public Guid targetuserid { get; set; }
        public SwipeAction action { get; set; }
        public DateTime swiped_at_utc { get; set; }
    }
}
