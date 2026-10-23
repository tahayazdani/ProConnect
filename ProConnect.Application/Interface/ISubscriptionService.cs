using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;

namespace ProConnect.Application.Interface
{
    /// <summary>
    /// Client-facing subscription/payment surface - SubscriptionController's whole backing service.
    /// Everything Stripe-specific is behind IPaymentGateway; everything plan/feature-specific is behind
    /// IFeatureGateService (which this service invalidates after any subscription-changing webhook).
    /// </summary>
    public interface ISubscriptionService
    {
        /// <summary>Public, unauthenticated-friendly catalog (is_active plans only) - a pricing page's
        /// data source.</summary>
        Task<ProConnectResponse<List<SubscriptionPlanResponse>>> GetPlans();

        /// <summary>Always returns something, even for a brand-new user with no subscription row - see
        /// Const.PlanCode remarks on the Free fallback.</summary>
        Task<ProConnectResponse<UserSubscriptionResponse>> GetMySubscription(Guid userid);

        /// <summary>Starts a Stripe Checkout session for a recurring plan - the response's checkout_url is
        /// what the client redirects/opens into. The actual plan change only takes effect once the
        /// checkout.session.completed webhook arrives (see HandleWebhook), not on this call.</summary>
        Task<ProConnectResponse<CheckoutResponse>> Checkout(Guid userid, CheckoutRequest request);

        /// <summary>Starts a Stripe Checkout session for a one-off consumable bundle purchase - same
        /// "takes effect on webhook, not on this call" caveat as Checkout.</summary>
        Task<ProConnectResponse<CheckoutResponse>> PurchaseConsumable(Guid userid, ConsumablePurchaseRequest request);

        /// <summary>Cancels the caller's active subscription. at_period_end = true (the default) keeps
        /// them entitled through the current billing period; false cancels immediately.</summary>
        Task<ProConnectResponse<string>> CancelSubscription(Guid userid, bool at_period_end = true);

        /// <summary>Entry point for the raw Stripe webhook payload - verifies the signature (via
        /// IPaymentGateway.ParseWebhookEvent, which throws on a bad signature) and applies whichever of
        /// checkout completed / subscription updated-or-canceled / invoice paid-or-failed it represents.
        /// Idempotent per Stripe event id - see PaymentTransactionEntity remarks.</summary>
        Task<ProConnectResponse<string>> HandleWebhook(string payload, string stripe_signature_header);

        Task<ProConnectResponse<TransactionPageResponse>> GetTransactions(Guid userid, TransactionPageRequest request);
    }
}
