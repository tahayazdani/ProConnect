using ProConnect.Domain.Const;

namespace ProConnect.Domain.Request
{
    /// <summary>
    /// Starts a Stripe Checkout session for a subscription plan - see ISubscriptionService.Checkout /
    /// IPaymentGateway.CreateCheckoutSession. The caller picks the plan by plan_code (Const.PlanCode.*,
    /// or any future code added purely via the subscription_plan table) rather than a planid, since the
    /// client-facing plan picker works off SubscriptionPlanResponse.plan_code.
    /// </summary>
    public class CheckoutRequest
    {
        public CheckoutRequest()
        {

        }
        public string plan_code { get; set; }
        public BillingInterval billing_interval { get; set; } = BillingInterval.Monthly;

        /// <summary>Where Stripe Checkout redirects after a successful/cancelled payment - required
        /// because this is backend-only (no fixed frontend URL to default to); see IMatchService-style
        /// backend/frontend split noted elsewhere in this codebase.</summary>
        public string success_url { get; set; }
        public string cancel_url { get; set; }
    }
}
