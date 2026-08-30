using System.Collections.Generic;

namespace ProConnect.Domain.Response
{
    public class SearchResponse
    {
        public SearchResponse()
        {

        }
        public List<SearchResultResponse> results { get; set; } = new();
        public int total_count { get; set; }
        public int page { get; set; }
        public int pagesize { get; set; }
    }
}
