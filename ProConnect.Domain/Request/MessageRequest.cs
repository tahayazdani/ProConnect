namespace ProConnect.Domain.Request
{
    /// <summary>
    /// Body for POST /message/v1/{matchId} - sends a plain-text message to the other side of an existing
    /// match. Plain text only in v1 (no attachments/rich content) - see PROJECT_NOTES.md Phase 5 decision.
    /// </summary>
    public class MessageRequest
    {
        public MessageRequest()
        {

        }
        public string content { get; set; }
    }
}
