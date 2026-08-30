using Microsoft.Extensions.Logging;
using ProConnect.Application.Interface;
using ProConnect.Domain.Const;
using ProConnect.Domain.Model;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Repository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProConnect.Application.Service
{
    public class SearchService : ISearchService
    {
        private readonly ISearchRepository _searchRepository;
        private readonly Cache _cache;
        private readonly ILogger<SearchService> _logger;

        public SearchService(ISearchRepository searchRepository, Cache cache, ILogger<SearchService> logger)
        {
            _searchRepository = searchRepository;
            _cache = cache;
            _logger = logger;
        }

        public async Task<ProConnectResponse<SearchResponse>> Search(Guid userid, SearchRequest request)
        {
            try
            {
                if (request is null)
                    return new ProConnectResponse<SearchResponse>
                    {
                        issuccessfull = false,
                        errormessage = "Request is required.",
                        message = "Request is required."
                    };

                var response = await _cache.Get(CacheKey(userid, request), async () =>
                {
                    var result = await _searchRepository.Search(request, userid);

                    return new SearchResponse
                    {
                        results = (result?.items ?? new List<UserRequest>())
                            .Where(c => c != null && c.userid != userid)
                            .Select(MapToResult)
                            .ToList(),
                        total_count = result?.totalcount ?? 0,
                        page = request.page,
                        pagesize = request.pagesize
                    };
                }, CacheRegion.SEARCHRESULT, CacheExpiry.SearchResult);

                return new ProConnectResponse<SearchResponse>
                {
                    body = response ?? new SearchResponse { page = request.page, pagesize = request.pagesize },
                    message = "Search results have been fetched successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Search failed for userid '{userid}'.");
                throw;
            }
        }

        // Distinct key per filter combination + page, scoped to the caller so one user's cached page
        // of results is never served to another.
        private static string CacheKey(Guid userid, SearchRequest request)
        {
            return $"search:{userid}:{request.query}:{request.city}:{request.state}:{request.country}:{request.industry}:{request.company}:{request.radius_km}:{request.page}:{request.pagesize}"
                .ToLowerInvariant();
        }

        private static SearchResultResponse MapToResult(UserRequest candidate) => new SearchResultResponse
        {
            userid = candidate.userid,
            public_identifier = candidate.public_identifier,
            full_name = candidate.full_name,
            headline = candidate.headline,
            profile_pic_url = candidate.profile_pic_url,
            occupation = candidate.occupation,
            company = candidate.company,
            city = candidate.city,
            state = candidate.state,
            country = candidate.country
        };
    }
}
