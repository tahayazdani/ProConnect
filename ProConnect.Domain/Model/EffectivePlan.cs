using System;
using System.Collections.Generic;

namespace ProConnect.Domain.Model
{
    /// <summary>
    /// The fully-resolved result of IFeatureGateService.ResolveEffectivePlan(userid) - what a user's plan
    /// *actually* grants them right now, folding together their UserSubscription row (or lack of one) with
    /// the SubscriptionPlan/PlanFeature catalog. This is the object cached under CacheRegion.EFFECTIVEPLAN
    /// (CacheExpiry.EffectivePlan), not the raw UserSubscription row, since this is what every feature check
    /// (IsEnabled/TryConsume) actually reads.
    /// </summary>
    public class EffectivePlan
    {
        public EffectivePlan()
        {

        }
        public Guid userid { get; set; }

        /// <summary>Const.PlanCode.* - Free if the user has no UserSubscription row at all, or if their
        /// subscription has lapsed (Canceled/Expired/PastDue) - see IFeatureGateService remarks.</summary>
        public string plan_code { get; set; }
        public string display_name { get; set; }

        /// <summary>Keyed by Const.FeatureKey.* (or any other feature_key present in plan_feature).</summary>
        public Dictionary<string, FeatureEntitlement> features { get; set; } = new();
    }
}
