using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProConnect.Application.Interface;
using ProConnect.Domain.Config;
using ProConnect.Domain.Const;
using ProConnect.Domain.Model;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Repository;

namespace ProConnect.Application.Service
{
    /// <summary>
    /// See IFeatureGateService remarks. ResolveEffectivePlan's result (plan + per-feature is_enabled/
    /// limit_value, plus a usage snapshot taken at cache-population time for display) is cached under
    /// CacheRegion.EFFECTIVEPLAN - same cache-aside pattern MatchService uses for CacheRegion.
    /// MATCHRECOMMENDATION. TryConsume deliberately does NOT trust the cached usage snapshot for its
    /// allow/deny decision (usage changes on every swipe, far more often than CacheExpiryConfig.effectiveplan's
    /// 10-minute TTL) - it re-reads ISubscriptionRepository.GetFeatureUsage live instead, only using the
    /// cached plan for the (rarely-changing) is_enabled/limit_value/reset_period part.
    /// </summary>
    public class FeatureGateService : IFeatureGateService
    {
        private readonly ISubscriptionRepository _subscriptionRepository;
        private readonly Cache _cache;
        private readonly CacheExpiryConfig _cacheExpiryConfig;
        private readonly ILogger<FeatureGateService> _logger;

        public FeatureGateService(ISubscriptionRepository subscriptionRepository, Cache cache, CacheExpiryConfig cacheExpiryConfig, ILogger<FeatureGateService> logger)
        {
            _subscriptionRepository = subscriptionRepository;
            _cache = cache;
            _cacheExpiryConfig = cacheExpiryConfig;
            _logger = logger;
        }

        public async Task<EffectivePlan> ResolveEffectivePlan(Guid userid)
        {
            return await _cache.Get(EffectivePlanCacheKey(userid), async () => await ComputeEffectivePlan(userid),
                CacheRegion.EFFECTIVEPLAN, TimeSpan.FromSeconds(_cacheExpiryConfig.effectiveplan));
        }

        public async Task<bool> IsEnabled(Guid userid, string feature_key)
        {
            var effective = await ResolveEffectivePlan(userid);
            return effective.features.TryGetValue(feature_key, out var entitlement) && entitlement.is_enabled;
        }

        public async Task<FeatureConsumeResult> TryConsume(Guid userid, string feature_key, int amount = 1)
        {
            var effective = await ResolveEffectivePlan(userid);

            if (!effective.features.TryGetValue(feature_key, out var entitlement) || !entitlement.is_enabled)
                return new FeatureConsumeResult { allowed = false, limit = null, used = 0 };

            // A plain flag has nothing to count - being enabled at all is the whole answer.
            if (entitlement.reset_period == ResetPeriod.None)
                return new FeatureConsumeResult { allowed = true, limit = null, used = 0 };

            var periodKey = DerivePeriodKey(entitlement.reset_period);

            // BUG-17 fix: this used to be a live GetFeatureUsage read, a check against
            // entitlement.limit_value, and (if it passed) a separate IncrementFeatureUsage write - three
            // steps, each its own round trip, with no lock held across them. Concurrent callers could all
            // run the read+check before any of them ran the write, all see the same stale usage count, all
            // pass, and all increment - overshooting the cap (BUGS_FOUND.md BUG-17). TryIncrementFeatureUsage
            // folds the check and the write into one atomic, row-locked Postgres statement: it returns the
            // new usage count if the increment was allowed, or null if applying it would have exceeded
            // limit_value (in which case no row was written, so usage is unchanged).
            var newUsed = await _subscriptionRepository.TryIncrementFeatureUsage(userid, feature_key, periodKey, amount, entitlement.limit_value);

            if (newUsed is null)
            {
                // Rejected - report the current usage for the error message/UI, same as before.
                var currentUsed = await _subscriptionRepository.GetFeatureUsage(userid, feature_key, periodKey);
                return new FeatureConsumeResult { allowed = false, limit = entitlement.limit_value, used = currentUsed };
            }

            return new FeatureConsumeResult { allowed = true, limit = entitlement.limit_value, used = newUsed.Value };
        }

        public async Task InvalidateEffectivePlan(Guid userid)
        {
            await _cache.Delete(EffectivePlanCacheKey(userid), CacheRegion.EFFECTIVEPLAN);
        }

        // BUG-04/BUG-11 fix: see IFeatureGateService remarks. Reuses the same reset_period -> periodKey
        // derivation TryConsume/ComputeEffectivePlan use, so the refund lands against the exact same
        // counter row the original TryConsume call incremented. Deliberately a no-op (not an error) when
        // the feature isn't a counted one for this user - a caller that already successfully consumed a
        // unit from it obviously found one to consume, but staying defensive here costs nothing and avoids
        // a NullReferenceException if entitlements changed between the consume and the refund.
        public async Task RefundConsumption(Guid userid, string feature_key, int amount = 1)
        {
            var effective = await ResolveEffectivePlan(userid);

            if (!effective.features.TryGetValue(feature_key, out var entitlement) || entitlement.reset_period == ResetPeriod.None)
                return;

            var periodKey = DerivePeriodKey(entitlement.reset_period);
            await _subscriptionRepository.IncrementFeatureUsage(userid, feature_key, periodKey, -amount);
        }

        private async Task<EffectivePlan> ComputeEffectivePlan(Guid userid)
        {
            var subscription = await _subscriptionRepository.GetUserSubscription(userid);

            // No row at all, or a lapsed subscription, both resolve to Free - see Const.PlanCode remarks.
            var isLapsed = subscription != null && subscription.status != SubscriptionStatus.Active;
            var plan = (subscription is null || isLapsed)
                ? await _subscriptionRepository.GetPlanByCode(PlanCode.Free)
                : await _subscriptionRepository.GetPlanById(subscription.planid);

            // Defensive fallback in case a user's plan was deactivated/removed out from under them -
            // Free must always exist and stay active (see Const.PlanCode remarks), so this should only
            // ever trigger on a data-integrity problem, never in normal operation.
            if (plan is null)
            {
                _logger.LogWarning("FeatureGateService could not resolve plan for userid '{UserId}' - falling back to Free.", userid);
                plan = await _subscriptionRepository.GetPlanByCode(PlanCode.Free);
            }

            var result = new EffectivePlan
            {
                userid = userid,
                plan_code = plan?.plan_code ?? PlanCode.Free,
                display_name = plan?.display_name ?? "Free"
            };

            if (plan is null)
                return result;

            foreach (var feature in plan.features)
            {
                var entitlement = new FeatureEntitlement
                {
                    feature_key = feature.feature_key,
                    is_enabled = feature.is_enabled,
                    limit_value = feature.limit_value,
                    reset_period = feature.reset_period
                };

                if (feature.is_enabled && feature.reset_period != ResetPeriod.None)
                {
                    var periodKey = DerivePeriodKey(feature.reset_period);
                    entitlement.used = await _subscriptionRepository.GetFeatureUsage(userid, feature.feature_key, periodKey);
                }

                result.features[feature.feature_key] = entitlement;
            }

            return result;
        }

        // Daily resets at UTC midnight, monthly on the 1st UTC - a new period simply starts a fresh
        // UserFeatureUsageEntity row (see its remarks) rather than needing a reset job to zero anything out.
        private static string DerivePeriodKey(ResetPeriod resetPeriod) => resetPeriod switch
        {
            ResetPeriod.Daily => DateTime.UtcNow.ToString("yyyy-MM-dd"),
            ResetPeriod.Monthly => DateTime.UtcNow.ToString("yyyy-MM"),
            _ => "none"
        };

        private static string EffectivePlanCacheKey(Guid userid) => $"effectiveplan:{userid}";
    }
}
