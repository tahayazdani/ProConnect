namespace ProConnect.Domain.Response
{
    /// <summary>
    /// Client-facing wrapper around Model.CheckoutSessionResult, returned by both
    /// ISubscriptionService.Checkout and PurchaseConsumable - just the redirect target, nothing Stripe-specific.
    /// </summary>
    public class CheckoutResponse
    {
        public CheckoutResponse()
        {

        }
        public string checkout_url { get; set; }
    }
}
