namespace ProConnect.Domain.Request
{
    /// <summary>
    /// Pagination request for ISubscriptionService.GetTransactions - the user's own PaymentTransactionEntity
    /// history (subscriptions and consumable purchases), most-recent-first. Same shape as
    /// MatchCandidatePageRequest minus `refresh`, since transaction history is never cached (it's a direct,
    /// infrequently-hit DB-level offset/limit read - see ISubscriptionRepository.GetTransactions).
    /// </summary>
    public class TransactionPageRequest
    {
        public TransactionPageRequest()
        {

        }
        public int page { get; set; } = 1;
        public int pagesize { get; set; } = 50;
    }
}
