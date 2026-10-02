using System;

namespace ProConnect.Domain.Response
{
    public class MessageResponse
    {
        public MessageResponse()
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
