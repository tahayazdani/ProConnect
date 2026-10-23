using System;

namespace ProConnect.Domain.Model
{
    /// <summary>
    /// A purchasable à la carte bundle - see Infrastructure.Persistence.Entity.ProductEntity for why this
    /// is separate from SubscriptionPlan (one-off purchase catalog vs. recurring plan catalog).
    /// </summary>
    public class Product
    {
        public Product()
        {

        }
        public Guid productid { get; set; }

        /// <summary>One of Const.ConsumableKey.* - which wallet (UserConsumableBalanceEntity.feature_key)
        /// a purchase of this product credits.</summary>
        public string consumable_key { get; set; }
        public string display_name { get; set; }
        public string description { get; set; }
        public int quantity_granted { get; set; }
        public decimal price { get; set; }
        public string currency { get; set; } = "usd";
        public string stripe_price_id { get; set; }
        public bool is_active { get; set; } = true;
    }
}
