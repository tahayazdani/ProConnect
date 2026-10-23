using System;
using System.Threading.Tasks;
using ProConnect.Domain.Const;
using ProConnect.Domain.Model;

namespace ProConnect.Infrastructure.Interface
{
    /// <summary>
    /// Seam between ISubscriptionService and whichever payment provider is actually wired up -
    /// StripePaymentGateway today, but nothing above this interface (SubscriptionService, the controller,
    /// FeatureGateService) knows or cares that it's Stripe. Swapping providers, or just rotating keys, is
    /// "plug in by API key" - edit StripeConfig, no code change elsewhere. See PROJECT_NOTES.md Phase 7
    /// decision and IGeocodingProvider for the same seam pattern used elsewhere in this codebase.
    /// </summary>
    public interface IPaymentGateway
    {
        /// <summary>
        /// Starts a recurring-subscription Checkout session for the given plan/interval. existing_stripe_customer_id
        /// lets a returning customer (upgrading/downgrading, or re-subscribing) reuse their Stripe Customer
        /// record instead of creating a duplicate one - pass null for a brand-new customer.
        /// </summary>
        Task<CheckoutSessionResult> CreateSubscriptionCheckoutSession(
            Guid userid,
            SubscriptionPlan plan,
            BillingInterval interval,
            string success_url,
            string cancel_url,
            string existing_stripe_customer_id);

        /// <summary>
        /// Starts a one-off payment Checkout session for an à la carte consumable bundle.
        /// </summary>
        Task<CheckoutSessionResult> CreateConsumableCheckoutSession(
            Guid userid,
            Product product,
            int quantity,
            string success_url,
            string cancel_url,
            string existing_stripe_customer_id);

        /// <summary>
        /// Cancels a subscription - at_period_end = true keeps it entitled through the current billing
        /// period (UserSubscription.cancel_at_period_end), false cancels immediately.
        /// </summary>
        Task CancelSubscription(string stripe_subscription_id, bool at_period_end);

        /// <summary>
        /// Verifies the inbound webhook's signature against StripeConfig.webhooksecret and normalizes the
        /// handful of event types SubscriptionService.HandleWebhook actually acts on into a
        /// PaymentWebhookEvent. Throws if the signature doesn't verify - the controller action should
        /// return 400 in that case, never acting on an unverified payload.
        /// </summary>
        PaymentWebhookEvent ParseWebhookEvent(string payload, string stripe_signature_header);
    }
}
