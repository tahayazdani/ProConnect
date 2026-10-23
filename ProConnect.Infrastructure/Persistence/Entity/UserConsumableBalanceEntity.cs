using System;

namespace ProConnect.Infrastructure.Persistence.Entity
{
    /// <summary>
    /// The wallet side of à la carte purchases - one row per (userid, feature_key) tracking how many units
    /// of a Const.ConsumableKey a user currently holds. Credited by SubscriptionService.HandleWebhook on a
    /// successful consumable checkout, debited wherever the consumable is actually spent (e.g. sending a
    /// Rose). Composite PK, same upsert shape as MatchCriteriaEntity/UserSubscriptionEntity.
    /// </summary>
    public class UserConsumableBalanceEntity
    {
        public Guid userid { get; set; }
        public string feature_key { get; set; }
        public int balance { get; set; }
        public DateTime updated_at_utc { get; set; }
    }
}
