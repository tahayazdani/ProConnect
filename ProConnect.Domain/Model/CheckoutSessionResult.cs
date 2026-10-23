namespace ProConnect.Domain.Model
{
    /// <summary>
    /// Return value of IPaymentGateway.CreateCheckoutSession / CreateConsumableCheckoutSession - deliberately
    /// thin (just what SubscriptionController hands back to redirect the client into Stripe Checkout) so the
    /// rest of the codebase never touches a Stripe SDK type directly. Everything beyond this - what actually
    /// happened to the purchase - arrives later as a webhook, parsed into PaymentWebhookEvent.
    /// </summary>
    public class CheckoutSessionResult
    {
        public CheckoutSessionResult()
        {

        }
        public string session_id { get; set; }

        /// <summary>Stripe-hosted Checkout page the client should redirect/open to.</summary>
        public string checkout_url { get; set; }
    }
}
