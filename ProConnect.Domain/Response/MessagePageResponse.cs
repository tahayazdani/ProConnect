using System.Collections.Generic;

namespace ProConnect.Domain.Response
{
    /// <summary>
    /// Paged wrapper around MessageResponse - most-recent-first (same ordering convention as
    /// MatchSummaryPageResponse/GetMatches), DB-level offset/limit (see IMessageRepository.GetMessages) so
    /// a long-running conversation's full history is never materialized just to serve one page.
    /// </summary>
    public class MessagePageResponse
    {
        public MessagePageResponse()
        {

        }
        public List<MessageResponse> results { get; set; } = new();
        public int total_count { get; set; }
        public int page { get; set; }
        public int pagesize { get; set; }
        public bool has_more { get; set; }
    }
}
