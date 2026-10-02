using System;

namespace ProConnect.Domain.Model
{
    /// <summary>
    /// A single row from MessageRepository - mirrors MessageEntity but keeps the Domain project EF-free,
    /// same reasoning as MatchSummary.
    /// </summary>
    public class Message
    {
        public Message()
        {

        }
        public Guid messageid { get; set; }
        public Guid matchid { get; set; }
        public Guid senderuserid { get; set; }
        public string content { get; set; }
        public DateTime sent_at_utc { get; set; }
        public bool is_read { get; set; }
        public DateTime? read_at_utc { get; set; }
    }
}
