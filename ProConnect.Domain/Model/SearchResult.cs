using ProConnect.Domain.Request;
using System.Collections.Generic;

namespace ProConnect.Domain.Model
{
    /// <summary>
    /// Repository-level result - `totalcount` reflects the full match count for the filters, not just
    /// `items.Count`, so the real DB implementation can paginate in SQL instead of loading everything
    /// into memory.
    /// </summary>
    public class SearchResult
    {
        public SearchResult()
        {

        }
        public List<UserRequest> items { get; set; } = new();
        public int totalcount { get; set; }
    }
}
