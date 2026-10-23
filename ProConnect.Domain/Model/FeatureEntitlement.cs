using ProConnect.Domain.Const;

namespace ProConnect.Domain.Model
{
    /// <summary>
    /// One feature_key's resolved entitlement for a specific user - the value object stored in
    /// EffectivePlan.features. Unlike PlanFeature (which just describes a plan's catalog entry),
    /// this additionally carries the user's current usage for a counted feature, so a caller can see
    /// both the cap and how much of it is already spent without a second call to FeatureGateService.
    /// </summary>
    public class FeatureEntitlement
    {
        public FeatureEntitlement()
        {

        }
        public string feature_key { get; set; }
        public bool is_enabled { get; set; }
        public int? limit_value { get; set; }
        public ResetPeriod reset_period { get; set; } = ResetPeriod.None;

        /// <summary>Units already consumed in the current reset_period window (see
        /// FeatureGateService's period-key derivation / UserFeatureUsageEntity). Null for a plain flag
        /// (reset_period == None), where usage tracking doesn't apply.</summary>
        public int? used { get; set; }

        /// <summary>Convenience - true when this is a counted feature and used has reached limit_value.
        /// Always false for a plain flag or an unlimited (limit_value == null) counted feature.</summary>
        public bool is_exhausted => reset_period != ResetPeriod.None && limit_value.HasValue && used.HasValue && used.Value >= limit_value.Value;
    }
}
