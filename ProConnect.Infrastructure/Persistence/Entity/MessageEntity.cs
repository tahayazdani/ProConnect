using System;

namespace ProConnect.Infrastructure.Persistence.Entity
{
    /// <summary>
    /// A single chat message, scoped directly to a match (matchid) rather than a separate conversations
    /// table - a match already uniquely identifies the two-party relationship, so a conversation is just
    /// "the messages for this matchid". See MessageEntityConfiguration for why this cascades off
    /// MatchEntity: unmatching (MatchRepository.Unmatch) hard-deletes the matches row, and messages for a
    /// dissolved match should disappear with it rather than needing bespoke cleanup code.
    /// </summary>
    public class MessageEntity
    {
        public Guid messageid { get; set; }
        public Guid matchid { get; set; }
        public Guid senderuserid { get; set; }
        public string content { get; set; }
        public DateTime sent_at_utc { get; set; }

        // Read-receipt pair - set together the first time the *other* party fetches the conversation after
        // this message was sent (see MessageRepository.MarkAsRead, called from MessageService.GetMessages).
        // Kept as a plain boolean + nullable timestamp (rather than, say, a separate read-receipts table)
        // since each message only ever has exactly one recipient to track here (matchid is a 1:1 pairing).
        public bool is_read { get; set; }
        public DateTime? read_at_utc { get; set; }
    }
}
