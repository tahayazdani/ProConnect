using System;

namespace ProConnect.Infrastructure.Persistence.Entity
{
    /// <summary>
    /// A purchasable à la carte bundle backing a Const.ConsumableKey (e.g. "5 Roses for $9.99") - the
    /// Stripe line item for ISubscriptionService.PurchaseConsumable's checkout session. Separate from
    /// SubscriptionPlanEntity since this is a one-off purchase catalog, not a recurring plan; multiple
    /// products can share the same consumable_key at different bundle sizes/prices (e.g. a 5-pack and a
    /// 20-pack of Roses both crediting UserConsumableBalanceEntity under "rose").
    /// </summary>
    public class ProductEntity
    {
        public Guid productid { get; set; }
        public string consumable_key { get; set; }
        public string display_name { get; set; }
        public string? description { get; set; }

        /// <summary>How many units of consumable_key one purchase of this product credits - e.g. 5 for a
        /// "5 Roses" bundle.</summary>
        public int quantity_granted { get; set; }
        public decimal price { get; set; }
        public string currency { get; set; } = "usd";
        public string? stripe_price_id { get; set; }
        public bool is_active { get; set; } = true;
    }
}
