using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using System;
using System.Threading.Tasks;

namespace ProConnect.Application.Interface
{
    public interface ISearchService
    {
        Task<ProConnectResponse<SearchResponse>> Search(Guid userid, SearchRequest request);
    }
}
