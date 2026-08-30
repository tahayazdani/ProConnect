using System;

namespace ProConnect.Infrastructure.Persistence.Entity
{
    /// <summary>
    /// EF Core persistence model mirroring ProConnect.Domain.Model.MatchCriteria - one row per user,
    /// upserted by MatchCriteriaRepository. userid is both the primary key and the FK to UserEntity.
    /// </summary>
    public class MatchCriteriaEntity
    {
        public Guid userid { get; set; }
        public string? city { get; set; }
        public string? state { get; set; }
        public string? country { get; set; }
        public string? industry { get; set; }
        public int? min_age { get; set; }
        public int? max_age { get; set; }
        public string[]? interests { get; set; }
    }
}
