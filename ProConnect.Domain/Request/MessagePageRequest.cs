namespace ProConnect.Domain.Request
{
    /// <summary>
    /// Pagination request for GET /message/v1/{matchId}. Deliberately its own type rather than reusing
    /// MatchCandidatePageRequest - message history is never cached (see IMessageService.GetMessages), so
    /// there's no equivalent of MatchCandidatePageRequest.refresh to carry here.
    /// </summary>
    public class MessagePageRequest
    {
        public MessagePageRequest()
        {

        }
        public int page { get; set; } = 1;
        public int pagesize { get; set; } = 50;
    }
}
