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

        /// <summary>Unconditional increment-or-insert, returns the usage count after incrementing. Only
        /// safe for callers that don't need a limit enforced atomically with the write - today that's
        /// RefundConsumption's negative-amount credit-back (a refund should never be blocked by the cap)
        /// - see UserFeatureUsageEntity remarks for why this isn't a Redis atomic increment or raw SQL
        /// upsert. BUG-17: this used to also back the admission-control path in FeatureGateService.
        /// TryConsume, preceded by a separate GetFeatureUsage read to check the limit - that two-step
        /// read-then-write was not atomic, so concurrent callers could all read the same stale usage count,
        /// all pass the check, and all increment, overshooting the cap (reproduced in BUGS_FOUND.md
        /// BUG-17). TryConsume now calls TryIncrementFeatureUsage instead, which folds the check into the
        /// same statement as the write.</summary>
        Task<int> IncrementFeatureUsage(Guid userid, string feature_key, string period_key, int amount = 1);

        /// <summary>Atomic check-and-increment for admission control: increments used_count by `amount`
        /// and returns the new value, but ONLY if the result would not exceed `limit` (null limit = no
        /// cap, always allowed) - the check and the write happen in the same Postgres statement, row-locked
        /// at the DB level, so concurrent callers serialize instead of all reading the same stale usage
        /// count the way the old GetFeatureUsage-then-IncrementFeatureUsage pattern allowed (BUG-17).
        /// Returns null when the increment was rejected because it would have exceeded `limit` - no row is
        /// written in that case. See SubscriptionRepository for the exact SQL.</summary>
        Task<int?> TryIncrementFeatureUsage(Guid userid, string feature_key, string period_key, int amount, int? limit);

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

        /// <summary>BUG-19 fix: HandleWebhook's idempotency guard used to be a bare
        /// TransactionExistsByStripeEventId check followed, several non-atomic repository calls later, by
        /// CreateTransaction's insert - with the side-effect calls (AdjustConsumableBalance /
        /// UpsertUserSubscription / etc.) sandwiched in between. Two genuinely concurrent deliveries of the
        /// identical stripe_event_id (not just a sequential Stripe retry, which this guard already handled
        /// fine) could both pass the exists-check before either had inserted its transaction row, both
        /// apply the side effect, and only collide at the very end on CreateTransaction's unique-index
        /// insert - by which point the double side effect (e.g. a consumable balance credited twice for
        /// one purchase) had already landed, with the loser also surfacing an ugly unhandled-exception 500
        /// instead of the same idempotent "already processed" response a sequential replay gets (reproduced
        /// in BUGS_FOUND.md BUG-19). AcquireEventLock takes a session-level Postgres advisory lock
        /// (pg_advisory_lock, hashed from stripe_event_id - same keyed-serialization idea as
        /// MatchRepository's pg_advisory_xact_lock use in RecordSwipe/CreateMatch, just session- rather
        /// than transaction-scoped because the critical section it guards spans multiple separate
        /// repository calls/DbContexts, not one). HandleWebhook holds it for the full duration of
        /// processing a given event_id, so a concurrent second delivery blocks until the first has fully
        /// committed (including CreateTransaction), then correctly sees TransactionExistsByStripeEventId
        /// return true and exits via the ordinary idempotent path - no double side effect, no 500. Disposing
        /// the returned handle releases the lock and the connection that holds it.</summary>
        Task<IAsyncDisposable> AcquireEventLock(string stripe_event_id);
    }
}
