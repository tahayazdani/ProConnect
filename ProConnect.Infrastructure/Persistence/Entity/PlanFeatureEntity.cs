using System;
using ProConnect.Domain.Const;

namespace ProConnect.Infrastructure.Persistence.Entity
{
    /// <summary>
    /// EF Core persistence model mirroring ProConnect.Domain.Model.PlanFeature - see that type's remarks
    /// for why this table is the entire "move a feature between tiers by flipping a flag" mechanism.
    /// </summary>
    public class PlanFeatureEntity
    {
        public Guid planfeatureid { get; set; }
        public Guid planid { get; set; }
        public string feature_key { get; set; }
        public bool is_enabled { get; set; }
        public int? limit_value { get; set; }
        public ResetPeriod reset_period { get; set; } = ResetPeriod.None;
    }
}
