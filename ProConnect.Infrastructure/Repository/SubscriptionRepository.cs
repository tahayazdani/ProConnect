using Microsoft.EntityFrameworkCore;
using ProConnect.Domain.Const;
using ProConnect.Domain.Model;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Persistence;
using ProConnect.Infrastructure.Persistence.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Repository
{
    public class SubscriptionRepository : ISubscriptionRepository
    {
        private readonly IDbContextFactory<ProConnectDbContext> _dbContextFactory;

        public SubscriptionRepository(IDbContextFactory<ProConnectDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public async Task<List<SubscriptionPlan>> GetActivePlans()
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var plans = await db.SubscriptionPlans.AsNoTracking()
                .Where(p => p.is_active)
                .OrderBy(p => p.sort_order)
                .ToListAsync();

            return await AttachFeatures(db, plans);
        }

        public async Task<SubscriptionPlan> GetPlanByCode(string plan_code)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var plan = await db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.plan_code == plan_code);
            if (plan is null) return null;

            var result = await AttachFeatures(db, new List<SubscriptionPlanEntity> { plan });
            return result.Single();
        }

        public async Task<SubscriptionPlan> GetPlanById(Guid planid)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var plan = await db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.planid == planid);
            if (plan is null) return null;

            var result = await AttachFeatures(db, new List<SubscriptionPlanEntity> { plan });
            return result.Single();
        }

        public async Task<SubscriptionPlan> GetPlanByStripePriceId(string stripe_price_id)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var plan = await db.SubscriptionPlans.AsNoTracking()
                .FirstOrDefaultAsync(p => p.stripe_price_id_monthly == stripe_price_id || p.stripe_price_id_annual == stripe_price_id);
            if (plan is null) return null;

            var result = await AttachFeatures(db, new List<SubscriptionPlanEntity> { plan });
            return result.Single();
        }

        private static async Task<List<SubscriptionPlan>> AttachFeatures(ProConnectDbContext db, List<SubscriptionPlanEntity> plans)
        {
            if (plans.Count == 0) return new List<SubscriptionPlan>();

            var planIds = plans.Select(p => p.planid).ToList();
            var features = await db.PlanFeatures.AsNoTracking()
                .Where(f => planIds.Contains(f.planid))
                .ToListAsync();
            var featuresByPlan = features.GroupBy(f => f.planid).ToDictionary(g => g.Key, g => g.ToList());

            return plans.Select(p => new SubscriptionPlan
            {
                planid = p.planid,
                plan_code = p.plan_code,
                display_name = p.display_name,
                description = p.description,
                price_monthly = p.price_monthly,
                price_annual = p.price_annual,
                stripe_price_id_monthly = p.stripe_price_id_monthly,
                stripe_price_id_annual = p.stripe_price_id_annual,
                is_active = p.is_active,
                sort_order = p.sort_order,
                features = featuresByPlan.TryGetValue(p.planid, out var planFeatures)
                    ? planFeatures.Select(ToDomain).ToList()
                    : new List<PlanFeature>()
            }).ToList();
        }

        private static PlanFeature ToDomain(PlanFeatureEntity f) => new PlanFeature
        {
            planfeatureid = f.planfeatureid,
            planid = f.planid,
            feature_key = f.feature_key,
            is_enabled = f.is_enabled,
            limit_value = f.limit_value,
            reset_period = f.reset_period
        };

        public async Task<UserSubscription> GetUserSubscription(Guid userid)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.UserSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.userid == userid);
            return entity is null ? null : ToDomain(entity);
        }

        public async Task<UserSubscription> GetUserSubscriptionByStripeSubscriptionId(string stripe_subscription_id)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.UserSubscriptions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.stripe_subscription_id == stripe_subscription_id);
            return entity is null ? null : ToDomain(entity);
        }

        // Same full-replacement-upsert shape as MatchCriteriaRepository.Upsert.
        public async Task<UserSubscription> UpsertUserSubscription(UserSubscription subscription)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.UserSubscriptions.FirstOrDefaultAsync(s => s.userid == subscription.userid);
            var isNew = entity is null;
            if (isNew)
            {
                entity = new UserSubscriptionEntity
                {
                    usersubscriptionid = Guid.NewGuid(),
                    userid = subscription.userid,
                    created_at_utc = DateTime.UtcNow
                };
                db.UserSubscriptions.Add(entity);
            }

            entity.planid = subscription.planid;
            entity.status = subscription.status;
            entity.billing_interval = subscription.billing_interval;
            entity.stripe_customer_id = subscription.stripe_customer_id;
            entity.stripe_subscription_id = subscription.stripe_subscription_id;
            entity.current_period_start_utc = subscription.current_period_start_utc;
            entity.current_period_end_utc = subscription.current_period_end_utc;
            entity.cancel_at_period_end = subscription.cancel_at_period_end;
            entity.updated_at_utc = DateTime.UtcNow;

            await db.SaveChangesAsync();

            return ToDomain(entity);
        }

        private static UserSubscription ToDomain(UserSubscriptionEntity e) => new UserSubscription
        {
            usersubscriptionid = e.usersubscriptionid,
            userid = e.userid,
            planid = e.planid,
            status = e.status,
            billing_interval = e.billing_interval,
            stripe_customer_id = e.stripe_customer_id,
            stripe_subscription_id = e.stripe_subscription_id,
            current_period_start_utc = e.current_period_start_utc,
            current_period_end_utc = e.current_period_end_utc,
            cancel_at_period_end = e.cancel_at_period_end,
            created_at_utc = e.created_at_utc,
            updated_at_utc = e.updated_at_utc
        };

        public async Task<int> GetFeatureUsage(Guid userid, string feature_key, string period_key)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.UserFeatureUsages.AsNoTracking()
                .FirstOrDefaultAsync(u => u.userid == userid && u.feature_key == feature_key && u.period_key == period_key);
            return entity?.used_count ?? 0;
        }

        // BUG-10 fix: this used to be a plain EF read-then-increment-or-insert (see the superseded comment
        // this replaced, and the still-accurate "no Redis atomic increment primitive" note on
        // UserFeatureUsageEntity). Under concurrent swipes for the same user/feature/period that pattern
        // either lost one side's increment (both sides read the same starting count, last SaveChangesAsync
        // wins) or threw a raw Npgsql 23505 unique-violation when both sides tried to INSERT the same
        // (userid, feature_key, period_key) row at once - reproduced in BUGS_FOUND.md BUG-10. Postgres's
        // INSERT ... ON CONFLICT DO UPDATE is a single atomic statement evaluated row-locked at the DB
        // level, so concurrent callers serialize on the row instead of racing in application memory - the
        // RETURNING clause hands back the post-increment value in the same statement, no separate read
        // needed. This is a deliberate, narrow exception to the EF-LINQ-only convention noted on
        // UserFeatureUsageEntity: there's no other atomic-increment primitive available for this exact race.
        // Callers already checked the limit via GetFeatureUsage first; TryConsume also uses this (with a
        // negative amount) to refund a charge that turned out not to correspond to a net-new Like - see
        // MatchService.Swipe / FeatureGateService remarks for BUG-04/BUG-11.
        public async Task<int> IncrementFeatureUsage(Guid userid, string feature_key, string period_key, int amount = 1)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();
            var now = DateTime.UtcNow;

            var result = await db.Database.SqlQuery<int>($@"
                INSERT INTO user_feature_usage (userid, feature_key, period_key, used_count, updated_at_utc)
                VALUES ({userid}, {feature_key}, {period_key}, {amount}, {now})
                ON CONFLICT (userid, feature_key, period_key)
                DO UPDATE SET used_count = user_feature_usage.used_count + EXCLUDED.used_count,
                              updated_at_utc = EXCLUDED.updated_at_utc
                RETURNING used_count").ToListAsync();

            return result.Single();
        }

        // BUG-17 fix: the admission-control path used to call GetFeatureUsage (a plain read) and then,
        // separately, IncrementFeatureUsage (the atomic-but-unconditional upsert above) from
        // FeatureGateService.TryConsume. Each call opened its own transaction, so two concurrent callers
        // could both run GetFeatureUsage before either had run IncrementFeatureUsage, both observe e.g.
        // used_count=9 against a limit of 10, both conclude "allowed", and both increment - landing at 11,
        // not 10 (reproduced in BUGS_FOUND.md BUG-17; a 15-way concurrent probe against a 10/day cap let
        // all 15 through). The fix folds the limit check into the same statement as the write so there is
        // no gap between "check" and "use" for a concurrent caller to land in:
        //  - The INSERT...SELECT branch (brand new row, i.e. first use in this period) only produces a row
        //    to insert when amount <= limit, via the WHERE on the SELECT.
        //  - The ON CONFLICT DO UPDATE branch (row already exists) only applies when
        //    existing used_count + amount <= limit, via the WHERE on the DO UPDATE.
        // Either way, Postgres evaluates the guard and performs the write as one row-locked statement, so
        // concurrent callers serialize on the row instead of racing in application memory. If the guard
        // fails, zero rows are affected/returned - no row is written (no insert, no update) - and this
        // returns null so the caller knows the increment was rejected, not merely "stayed the same".
        // limit = null means uncapped, matching FeatureEntitlement.limit_value's convention elsewhere.
        public async Task<int?> TryIncrementFeatureUsage(Guid userid, string feature_key, string period_key, int amount, int? limit)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();
            var now = DateTime.UtcNow;

            var result = await db.Database.SqlQuery<int>($@"
                INSERT INTO user_feature_usage (userid, feature_key, period_key, used_count, updated_at_utc)
                SELECT {userid}, {feature_key}, {period_key}, {amount}, {now}
                WHERE {limit} IS NULL OR {amount} <= {limit}
                ON CONFLICT (userid, feature_key, period_key)
                DO UPDATE SET used_count = user_feature_usage.used_count + {amount},
                              updated_at_utc = {now}
                WHERE {limit} IS NULL OR user_feature_usage.used_count + {amount} <= {limit}
                RETURNING used_count").ToListAsync();

            return result.Count == 0 ? (int?)null : result.Single();
        }

        public async Task<int> GetConsumableBalance(Guid userid, string feature_key)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.UserConsumableBalances.AsNoTracking()
                .FirstOrDefaultAsync(b => b.userid == userid && b.feature_key == feature_key);
            return entity?.balance ?? 0;
        }

        // BUG-18 fix: this used to be the exact same unprotected EF read-then-add-or-update-then-
        // SaveChangesAsync pattern that IncrementFeatureUsage had before BUG-10's fix - see the BUG-10
        // comment above for the general shape of the problem. user_consumable_balance has the identical
        // composite-PK-on-(userid, feature_key) shape as user_feature_usage, so it was exposed to the
        // identical race: two concurrent callers (e.g. two genuinely-concurrent webhook deliveries for the
        // same consumable purchase, or a purchase racing a refund) could both read the same starting
        // balance and both separately Add() a brand-new tracked entity when none existed yet, with two
        // possible outcomes depending on timing - either a lost update (both compute balance=delta against
        // the same stale read, last SaveChangesAsync wins, one side's credit/debit silently vanishes) or a
        // raw Npgsql 23505 unique-violation on PK_user_consumable_balance when both tried to INSERT the
        // same (userid, feature_key) row at once. Reproduced in BUGS_FOUND.md BUG-18 via two genuinely
        // concurrent identical-looking (but distinct-event-id) consumable-purchase webhooks for a
        // brand-new balance row: one request's INSERT landed, the other's died with "An error occurred
        // while saving the entity changes" (http 500) from the PK collision - and had both instead landed
        // as competing updates to an existing row, the loser's delta would have been silently lost instead
        // of erroring. Same fix shape as BUG-10: a single atomic INSERT ... ON CONFLICT DO UPDATE,
        // row-locked at the DB level, so concurrent callers serialize on the row instead of racing in
        // application memory.
        public async Task<int> AdjustConsumableBalance(Guid userid, string feature_key, int delta)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();
            var now = DateTime.UtcNow;

            var result = await db.Database.SqlQuery<int>($@"
                INSERT INTO user_consumable_balance (userid, feature_key, balance, updated_at_utc)
                VALUES ({userid}, {feature_key}, {delta}, {now})
                ON CONFLICT (userid, feature_key)
                DO UPDATE SET balance = user_consumable_balance.balance + {delta},
                              updated_at_utc = {now}
                RETURNING balance").ToListAsync();

            return result.Single();
        }

        public async Task<Product> GetActiveProductByConsumableKey(string consumable_key)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.consumable_key == consumable_key && p.is_active);
            return entity is null ? null : new Product
            {
                productid = entity.productid,
                consumable_key = entity.consumable_key,
                display_name = entity.display_name,
                description = entity.description,
                quantity_granted = entity.quantity_granted,
                price = entity.price,
                currency = entity.currency,
                stripe_price_id = entity.stripe_price_id,
                is_active = entity.is_active
            };
        }

        public async Task<List<PaymentTransaction>> GetTransactions(Guid userid, int offset, int limit)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entities = await db.PaymentTransactions.AsNoTracking()
                .Where(t => t.userid == userid)
                .OrderByDescending(t => t.created_at_utc).ThenBy(t => t.transactionid)
                .Skip(offset).Take(limit)
                .ToListAsync();

            return entities.Select(e => new PaymentTransaction
            {
                transactionid = e.transactionid,
                userid = e.userid,
                type = e.type,
                description = e.description,
                amount = e.amount,
                currency = e.currency,
                status = e.status,
                stripe_event_id = e.stripe_event_id,
                stripe_session_id = e.stripe_session_id,
                created_at_utc = e.created_at_utc
            }).ToList();
        }

        public async Task<int> CountTransactions(Guid userid)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();
            return await db.PaymentTransactions.AsNoTracking().CountAsync(t => t.userid == userid);
        }

        public async Task<PaymentTransaction> CreateTransaction(PaymentTransaction transaction)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = new PaymentTransactionEntity
            {
                transactionid = transaction.transactionid == Guid.Empty ? Guid.NewGuid() : transaction.transactionid,
                userid = transaction.userid,
                type = transaction.type,
                description = transaction.description,
                amount = transaction.amount,
                currency = transaction.currency,
                status = transaction.status,
                stripe_event_id = transaction.stripe_event_id,
                stripe_session_id = transaction.stripe_session_id,
                created_at_utc = transaction.created_at_utc == default ? DateTime.UtcNow : transaction.created_at_utc
            };
            db.PaymentTransactions.Add(entity);
            await db.SaveChangesAsync();

            transaction.transactionid = entity.transactionid;
            transaction.created_at_utc = entity.created_at_utc;
            return transaction;
        }

        public async Task<bool> TransactionExistsByStripeEventId(string stripe_event_id)
        {
            if (string.IsNullOrEmpty(stripe_event_id)) return false;

            await using var db = await _dbContextFactory.CreateDbContextAsync();
            return await db.PaymentTransactions.AsNoTracking().AnyAsync(t => t.stripe_event_id == stripe_event_id);
        }

        // BUG-19 fix - see ISubscriptionRepository.AcquireEventLock remarks for the full story. The
        // DbContext's connection is opened explicitly (rather than left to EF's normal open-per-command,
        // close-after-command default) because pg_advisory_lock is session-scoped: the lock has to be
        // taken and released on the exact same physical connection, held open across everything
        // HandleWebhook does in between - not just for the duration of a single SqlQuery call the way
        // every other method in this class uses its DbContext.
        public async Task<IAsyncDisposable> AcquireEventLock(string stripe_event_id)
        {
            var db = await _dbContextFactory.CreateDbContextAsync();
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock(hashtext({stripe_event_id}))");
            return new AdvisoryLockHandle(db, stripe_event_id);
        }

        private sealed class AdvisoryLockHandle : IAsyncDisposable
        {
            private readonly ProConnectDbContext _db;
            private readonly string _eventId;

            public AdvisoryLockHandle(ProConnectDbContext db, string eventId)
            {
                _db = db;
                _eventId = eventId;
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock(hashtext({_eventId}))");
                }
                finally
                {
                    await _db.Database.CloseConnectionAsync();
                    await _db.DisposeAsync();
                }
            }
        }
    }
}
