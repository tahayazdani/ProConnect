using System;
using ProConnect.Domain.Const;

namespace ProConnect.Infrastructure.Persistence.Entity
{
    /// <summary>
    /// One row per payment event (subscription checkout/renewal or consumable purchase) - the user-facing
    /// transaction history (ISubscriptionService.GetTransactions) and the idempotency record for inbound
    /// Stripe webhooks. stripe_event_id has a unique index (see configuration) - HandleWebhook checks for
    /// an existing row with the same event id before acting, so a Stripe retry (anything but a 2xx response)
    /// never double-applies (e.g. double-crediting a consumable).
    /// </summary>
    public class PaymentTransactionEntity
    {
        public Guid transactionid { get; set; }
        public Guid userid { get; set; }
        public PaymentTransactionType type { get; set; }
        public string? description { get; set; }
        public decimal amount { get; set; }
        public string currency { get; set; } = "usd";
        public PaymentStatus status { get; set; }
        public string? stripe_event_id { get; set; }
        public string? stripe_session_id { get; set; }
        public DateTime created_at_utc { get; set; }
    }
}
