using System;
using ProConnect.Domain.Const;

namespace ProConnect.Domain.Model
{
    /// <summary>
    /// One row from user_subscription - at most one per user (see SubscriptionRepository.GetUserSubscription),
    /// tracking which plan a user is actually paying for (or has paid for) and its Stripe-side identifiers.
    /// A brand-new user has no row here at all; FeatureGateService treats that the same as an Expired/Canceled
    /// row and falls back to Const.PlanCode.Free - see Const.PlanCode remarks and SubscriptionStatus remarks.
    /// </summary>
    public class UserSubscription
    {
        public UserSubscription()
        {

        }
        public Guid usersubscriptionid { get; set; }
        public Guid userid { get; set; }
        public Guid planid { get; set; }
        public string plan_code { get; set; }

        public SubscriptionStatus status { get; set; }
        public BillingInterval billing_interval { get; set; }

        /// <summary>Stripe's identifiers for this subscription - needed to look it up again on a webhook
        /// event or to drive cancel-at-period-end from SubscriptionService.</summary>
        public string stripe_customer_id { get; set; }
        public string stripe_subscription_id { get; set; }

        public DateTime current_period_start_utc { get; set; }
        public DateTime current_period_end_utc { get; set; }

        /// <summary>Set when the user has requested cancellation but is still entitled through
        /// current_period_end_utc (Stripe's "cancel at period end") - status stays Active until then.</summary>
        public bool cancel_at_period_end { get; set; }

        public DateTime created_at_utc { get; set; }
        public DateTime updated_at_utc { get; set; }
    }
}
