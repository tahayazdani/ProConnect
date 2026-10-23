using System;
using ProConnect.Domain.Const;

namespace ProConnect.Infrastructure.Persistence.Entity
{
    /// <summary>
    /// EF Core persistence model mirroring ProConnect.Domain.Model.UserSubscription - at most one row per
    /// user (unique index on userid, see UserSubscriptionEntityConfiguration), upserted the same way
    /// MatchCriteriaEntity is. A user with no row here is treated as Const.PlanCode.Free - see
    /// SubscriptionRepository.GetUserSubscription / IFeatureGateService remarks.
    /// </summary>
    public class UserSubscriptionEntity
    {
        public Guid usersubscriptionid { get; set; }
        public Guid userid { get; set; }
        public Guid planid { get; set; }
        public SubscriptionStatus status { get; set; }
        public BillingInterval billing_interval { get; set; }
        public string? stripe_customer_id { get; set; }
        public string? stripe_subscription_id { get; set; }
        public DateTime current_period_start_utc { get; set; }
        public DateTime current_period_end_utc { get; set; }
        public bool cancel_at_period_end { get; set; }
        public DateTime created_at_utc { get; set; }
        public DateTime updated_at_utc { get; set; }
    }
}
