using System;
using ProConnect.Domain.Const;

namespace ProConnect.Domain.Model
{
    /// <summary>
    /// Mirrors Infrastructure.Persistence.Entity.PaymentTransactionEntity - see that type's remarks on why
    /// stripe_event_id doubles as a webhook idempotency key.
    /// </summary>
    public class PaymentTransaction
    {
        public PaymentTransaction()
        {

        }
        public Guid transactionid { get; set; }
        public Guid userid { get; set; }
        public PaymentTransactionType type { get; set; }
        public string description { get; set; }
        public decimal amount { get; set; }
        public string currency { get; set; } = "usd";
        public PaymentStatus status { get; set; }
        public string stripe_event_id { get; set; }
        public string stripe_session_id { get; set; }
        public DateTime created_at_utc { get; set; }
    }
}
