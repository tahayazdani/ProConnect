using System;
using ProConnect.Domain.Const;

namespace ProConnect.Domain.Response
{
    /// <summary>
    /// "What am I currently on" - see ISubscriptionService.GetMySubscription. Always returns something even
    /// for a user with no user_subscription row at all: plan_code = Const.PlanCode.Free, status = Active,
    /// the other Stripe/billing fields left null - same Free-fallback logic as IFeatureGateService.
    /// ResolveEffectivePlan, just surfaced to the client instead of consumed internally.
    /// </summary>
    public class UserSubscriptionResponse
    {
        public UserSubscriptionResponse()
        {

        }
        public string plan_code { get; set; }
        public string display_name { get; set; }
        public SubscriptionStatus status { get; set; }
        public BillingInterval? billing_interval { get; set; }
        public DateTime? current_period_end_utc { get; set; }
        public bool cancel_at_period_end { get; set; }
    }
}
