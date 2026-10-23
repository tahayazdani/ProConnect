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

        // See UserFeatureUsageEntity remarks: plain EF read-then-increment-or-insert, not a Redis atomic
        // increment (no such primitive in ICache/Cache/RedisCache) or raw SQL upsert (this codebase's
        // EF Core LINQ-only convention). Callers already checked the limit via GetFeatureUsage first.
        public async Task<int> IncrementFeatureUsage(Guid userid, string feature_key, string period_key, int amount = 1)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.UserFeatureUsages
                .FirstOrDefaultAsync(u => u.userid == userid && u.feature_key == feature_key && u.period_key == period_key);
            if (entity is null)
            {
                entity = new UserFeatureUsageEntity
                {
                    userid = userid,
                    feature_key = feature_key,
                    period_key = period_key,
                    used_count = 0
                };
                db.UserFeatureUsages.Add(entity);
            }

            entity.used_count += amount;
            entity.updated_at_utc = DateTime.UtcNow;

            await db.SaveChangesAsync();

            return entity.used_count;
        }

        public async Task<int> GetConsumableBalance(Guid userid, string feature_key)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.UserConsumableBalances.AsNoTracking()
                .FirstOrDefaultAsync(b => b.userid == userid && b.feature_key == feature_key);
            return entity?.balance ?? 0;
        }

        public async Task<int> AdjustConsumableBalance(Guid userid, string feature_key, int delta)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.UserConsumableBalances
                .FirstOrDefaultAsync(b => b.userid == userid && b.feature_key == feature_key);
            if (entity is null)
            {
                entity = new UserConsumableBalanceEntity
                {
                    userid = userid,
                    feature_key = feature_key,
                    balance = 0
                };
                db.UserConsumableBalances.Add(entity);
            }

            entity.balance += delta;
            entity.updated_at_utc = DateTime.UtcNow;

            await db.SaveChangesAsync();

            return entity.balance;
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
    }
}
