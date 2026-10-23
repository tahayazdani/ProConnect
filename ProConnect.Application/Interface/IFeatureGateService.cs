using System;
using System.Threading.Tasks;
using ProConnect.Domain.Model;

namespace ProConnect.Application.Interface
{
    /// <summary>
    /// The runtime side of Phase 7's plan/feature system - every other service (MatchService today;
    /// MessageService once read-receipts/rewind are wired up) asks this, never the subscription tables
    /// directly, whether a user gets to do something. Entirely table-driven: see Const.PlanCode/FeatureKey
    /// and plan_feature's seed data for how a feature moves between tiers without a deploy.
    /// </summary>
    public interface IFeatureGateService
    {
        /// <summary>
        /// Resolves + caches (CacheRegion.EFFECTIVEPLAN, CacheExpiry.EffectivePlan) a user's plan and every
        /// feature it grants, folding in their current usage for counted features. Free (Const.PlanCode.Free)
        /// for a user with no subscription row at all, or whose subscription has lapsed - see
        /// Const.PlanCode remarks.
        /// </summary>
        Task<EffectivePlan> ResolveEffectivePlan(Guid userid);

        /// <summary>
        /// Plain on/off check for a flag feature (e.g. FeatureKey.WhoLikedMeVisible/RadiusSearchEnabled).
        /// Also works for a counted feature (true if the user has any entitlement to it at all, regardless
        /// of remaining usage) - use TryConsume instead when the point is to actually spend a unit.
        /// </summary>
        Task<bool> IsEnabled(Guid userid, string feature_key);

        /// <summary>
        /// Atomically-as-possible (see UserFeatureUsageEntity remarks on the documented rare-race
        /// trade-off) checks-and-increments usage of a counted feature (e.g. FeatureKey.DailyLikes) against
        /// the user's plan limit for the current reset_period window. Not enabled on the plan at all, or
        /// already at the limit, both return allowed = false without incrementing anything.
        /// </summary>
        Task<FeatureConsumeResult> TryConsume(Guid userid, string feature_key, int amount = 1);

        /// <summary>
        /// Clears the cached EffectivePlan for a user - called by SubscriptionService right after any
        /// subscription write (checkout completed, canceled, plan changed) so the new entitlements are
        /// visible on the very next check rather than waiting out CacheExpiry.EffectivePlan.
        /// </summary>
        Task InvalidateEffectivePlan(Guid userid);
    }
}
