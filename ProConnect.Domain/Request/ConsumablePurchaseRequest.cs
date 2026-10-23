namespace ProConnect.Domain.Request
{
    /// <summary>
    /// Starts a Stripe Checkout session for an à la carte consumable purchase (Const.ConsumableKey.*,
    /// e.g. a Rose/Boost bundle) - see ISubscriptionService.PurchaseConsumable. Separate endpoint/request
    /// from CheckoutRequest since this is a one-off payment (ProductEntity line item), not a recurring
    /// subscription, and credits a UserConsumableBalanceEntity row rather than a UserSubscription row.
    /// </summary>
    public class ConsumablePurchaseRequest
    {
        public ConsumablePurchaseRequest()
        {

        }
        /// <summary>One of Const.ConsumableKey.* - identifies which ProductEntity catalog row/price to
        /// check out.</summary>
        public string consumable_key { get; set; }
        public int quantity { get; set; } = 1;

        public string success_url { get; set; }
        public string cancel_url { get; set; }
    }
}
