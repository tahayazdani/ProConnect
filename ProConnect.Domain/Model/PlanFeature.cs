using System;
using ProConnect.Domain.Const;

namespace ProConnect.Domain.Model
{
    /// <summary>
    /// One row from plan_feature - whether a given plan unlocks a given Const.FeatureKey, and if it's a
    /// counted feature (ResetPeriod != None) what its limit is. This is the entire mechanism behind "move a
    /// feature from Free to Premium by flipping a flag" - see PROJECT_NOTES.md Phase 7 decision. No code
    /// change or deploy needed to retune limit_value or toggle is_enabled for an existing feature_key; only
    /// a brand-new feature_key needs a new Const.FeatureKey constant and a call site.
    /// </summary>
    public class PlanFeature
    {
        public PlanFeature()
        {

        }
        public Guid planfeatureid { get; set; }
        public Guid planid { get; set; }

        /// <summary>One of Const.FeatureKey.* by convention, but the DB does not enforce membership in
        /// that list - see Const.FeatureKey's remarks.</summary>
        public string feature_key { get; set; }

        /// <summary>For a plain on/off flag (reset_period == None) this is the whole answer. For a counted
        /// feature it gates whether the feature exists on this plan at all, independent of limit_value.</summary>
        public bool is_enabled { get; set; }

        /// <summary>Null for a plain flag. Set for a counted feature (e.g. daily_likes = 50) - the cap
        /// FeatureGateService.TryConsume enforces per reset_period window. Null + reset_period != None would
        /// mean "enabled with no cap", which the seed data never actually uses but the schema allows.</summary>
        public int? limit_value { get; set; }

        public ResetPeriod reset_period { get; set; } = ResetPeriod.None;
    }
}
