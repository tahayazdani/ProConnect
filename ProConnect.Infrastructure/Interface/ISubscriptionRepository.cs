using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProConnect.Domain.Model;

namespace ProConnect.Infrastructure.Interface
{
    /// <summary>
    /// Data access for the whole Phase 7 subscription/payment schema (subscription_plan, plan_feature,
    /// user_subscription, product, user_consumable_balance, user_feature_usage, payment_transaction).
    /// IFeatureGateService and ISubscriptionService are the only two callers - this repository has no
    /// opinion on caching (that's CacheRegion.EFFECTIVEPLAN, handled by FeatureGateService) or on Stripe
    /// (that's IPaymentGateway) - purely EF Core LINQ reads/writes, same split as the rest of this codebase.
    /// </summary>
    public interface ISubscriptionRepository
    {
        /// <summary>Catalog for GetPlans/checkout plan selection - is_active = true only, ordered by
        /// sort_order, each with its PlanFeature rows populated.</summary>
        Task<List<SubscriptionPlan>> GetActivePlans();

        /// <summary>Includes inactive plans deliberately (unlike GetActivePlans) - a user already on a
        /// retired plan still needs ResolveEffectivePlan/GetMySubscription to resolve it.</summary>
        Task<SubscriptionPlan> GetPlanByCode(string plan_code);
        Task<SubscriptionPlan> GetPlanById(Guid planid);

        /// <summary>Used by SubscriptionService.HandleWebhook to map a Stripe Price id (on a
        /// checkout.session.completed/invoice event) back to a plan.</summary>
        Task<SubscriptionPlan> GetPlanByStripePriceId(string stripe_price_id);

        /// <summary>Null when the user has no row at all (brand-new signup) - callers fall back to
        /// Const.PlanCode.Free, same convention as MatchCriteriaRepository.GetByUserId returning null.</summary>
        Task<UserSubscription> GetUserSubscription(Guid userid);
        Task<UserSubscription> GetUserSubscriptionByStripeSubscriptionId(string stripe_subscription_id);

        /// <summary>Full replacement upsert, same shape as MatchCriteriaRepository.Upsert - callers always
        /// send the complete row.</summary>
        Task<UserSubscription> UpsertUserSubscription(UserSubscription subscription);

        /// <summary>Current usage for a counted feature in the given period_key window (see
        /// UserFeatureUsageEntity remarks on period_key derivation) - 0 if no row exists yet.</summary>
        Task<int> GetFeatureUsage(Guid userid, string feature_key, string period_key);

        /// <summary>Read-then-increment-or-insert, returns the usage count after incrementing. Callers
        /// (FeatureGateService.TryConsume) are expected to have already checked GetFeatureUsage against the
        /// limit before calling this, so it unconditionally adds `amount` - see UserFeatureUsageEntity
        /// remarks for why this isn't a Redis atomic increment or raw SQL upsert.</summary>
        Task<int> IncrementFeatureUsage(Guid userid, string feature_key, string period_key, int amount = 1);

        Task<int> GetConsumableBalance(Guid userid, string feature_key);

        /// <summary>Adds delta (negative to spend, positive to credit) to the user's balance for
        /// feature_key, creating the row at 0 first if needed, and returns the resulting balance.</summary>
        Task<int> AdjustConsumableBalance(Guid userid, string feature_key, int delta);

        /// <summary>The single active product for a given Const.ConsumableKey - today's seed data has
        /// exactly one bundle per key, so "active" is enough to disambiguate; a future multi-bundle catalog
        /// (5-pack vs 20-pack) would need ConsumablePurchaseRequest to carry a productid instead.</summary>
        Task<Product> GetActiveProductByConsumableKey(string consumable_key);

        Task<List<PaymentTransaction>> GetTransactions(Guid userid, int offset, int limit);
        Task<int> CountTransactions(Guid userid);
        Task<PaymentTransaction> CreateTransaction(PaymentTransaction transaction);

        /// <summary>Idempotency check for HandleWebhook - see PaymentTransactionEntity remarks on why
        /// stripe_event_id is unique.</summary>
        Task<bool> TransactionExistsByStripeEventId(string stripe_event_id);
    }
}
