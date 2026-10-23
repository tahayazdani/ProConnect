using System;
using System.Collections.Generic;
using ProConnect.Domain.Const;

namespace ProConnect.Domain.Response
{
    /// <summary>
    /// One row of the user's own PaymentTransactionEntity history - see ISubscriptionService.GetTransactions.
    /// </summary>
    public class TransactionResponse
    {
        public TransactionResponse()
        {

        }
        public Guid transactionid { get; set; }
        public PaymentTransactionType type { get; set; }
        public string description { get; set; }
        public decimal amount { get; set; }
        public string currency { get; set; }
        public PaymentStatus status { get; set; }
        public DateTime created_at_utc { get; set; }
    }

    /// <summary>
    /// Paged wrapper, same shape as MatchCandidatePageResponse minus has_more's recomputation caveat -
    /// transaction history is a plain DB-level offset/limit read, never cached (see TransactionPageRequest).
    /// </summary>
    public class TransactionPageResponse
    {
        public TransactionPageResponse()
        {

        }
        public List<TransactionResponse> results { get; set; } = new();
        public int total_count { get; set; }
        public int page { get; set; }
        public int pagesize { get; set; }
        public bool has_more { get; set; }
    }
}
