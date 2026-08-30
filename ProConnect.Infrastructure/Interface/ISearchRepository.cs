using ProConnect.Domain.Model;
using ProConnect.Domain.Request;
using System;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Interface
{
    public interface ISearchRepository
    {
        /// <summary>
        /// Filters, excludes `excludeUserId`, and paginates - `SearchResult.totalcount` is the full
        /// match count for the filters (not just the page size), so the real DB implementation can
        /// paginate in SQL instead of loading every match into memory.
        /// </summary>
        Task<SearchResult> Search(SearchRequest request, Guid excludeUserId);
    }
}
