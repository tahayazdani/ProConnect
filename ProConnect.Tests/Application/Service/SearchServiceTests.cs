using Microsoft.Extensions.Logging;
using Moq;
using ProConnect.Application.Service;
using ProConnect.Domain.Config;
using ProConnect.Domain.Model;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Repository;

namespace ProConnect.Tests.Application.Service
{
    /// <summary>
    /// SearchService.Search is a single, thin method that always routes through the concrete Cache
    /// (see SearchService remarks / FeatureGateServiceTests for the same situation) before delegating to
    /// ISearchRepository.Search - there's no separate "radius search" method, the plain-text and
    /// PostGIS-radius paths are the same call site, branching only on whether SearchRequest.radius_km is
    /// set (that branch itself lives in the repository, not here). These tests confirm Search calls the
    /// repository with the exact request/excludeUserId, maps the repository's items (filtering out the
    /// caller) into SearchResultResponse, and carries totalcount/page/pagesize through correctly - for
    /// both an unset radius_km (plain-text) and a set radius_km (proximity) request.
    /// </summary>
    public class SearchServiceTests
    {
        private readonly Mock<ISearchRepository> _searchRepository = new();
        private readonly Mock<ICache> _icache = new();

        public SearchServiceTests()
        {
            _icache.Setup(c => c.GetCacheItem<CacheItem<SearchResponse>>(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(default(CacheItem<CacheItem<SearchResponse>>));
            _icache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<CacheItem<SearchResponse>>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpirationMode>()))
                .Returns(Task.CompletedTask);
        }

        private SearchService CreateService()
        {
            var cache = new Cache(new Mock<ILogger<Cache>>().Object, _icache.Object, new CacheAsideConfig { enablecacheaside = true });
            var cacheExpiryConfig = new CacheExpiryConfig { searchresult = 300 };

            return new SearchService(_searchRepository.Object, cache, cacheExpiryConfig, new Mock<ILogger<SearchService>>().Object);
        }

        [Fact]
        public async Task Search_WithNullRequest_ReturnsFailureWithoutCallingRepository()
        {
            var service = CreateService();

            var response = await service.Search(Guid.NewGuid(), null!);

            Assert.False(response.issuccessfull);
            Assert.Equal("Request is required.", response.errormessage);
            _searchRepository.Verify(r => r.Search(It.IsAny<SearchRequest>(), It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task Search_PlainTextRequest_DelegatesToRepositoryAndMapsResults()
        {
            var userid = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();
            var request = new SearchRequest { city = "London", page = 1, pagesize = 20 };

            _searchRepository.Setup(r => r.Search(request, userid)).ReturnsAsync(new SearchResult
            {
                totalcount = 1,
                items = new List<UserRequest>
                {
                    new UserRequest { userid = otherUserId, public_identifier = "jdoe", full_name = "Jane Doe", city = "London" }
                }
            });

            var service = CreateService();

            var response = await service.Search(userid, request);

            Assert.True(response.issuccessfull);
            Assert.NotNull(response.body);
            Assert.Single(response.body!.results);
            Assert.Equal(otherUserId, response.body!.results[0].userid);
            Assert.Equal("Jane Doe", response.body!.results[0].full_name);
            Assert.Equal(1, response.body!.total_count);
            _searchRepository.Verify(r => r.Search(request, userid), Times.Once);
        }

        [Fact]
        public async Task Search_ExcludesTheCallerFromResults()
        {
            var userid = Guid.NewGuid();
            var request = new SearchRequest { city = "London" };

            _searchRepository.Setup(r => r.Search(request, userid)).ReturnsAsync(new SearchResult
            {
                totalcount = 2,
                items = new List<UserRequest>
                {
                    new UserRequest { userid = userid, full_name = "Self" },
                    new UserRequest { userid = Guid.NewGuid(), full_name = "Other" }
                }
            });

            var service = CreateService();

            var response = await service.Search(userid, request);

            Assert.True(response.issuccessfull);
            Assert.Single(response.body!.results);
            Assert.Equal("Other", response.body!.results[0].full_name);
        }

        [Fact]
        public async Task Search_WithRadiusKmSet_PassesRequestThroughToRepositoryUnchanged()
        {
            var userid = Guid.NewGuid();
            var request = new SearchRequest { city = "London", radius_km = 25, page = 2, pagesize = 10 };

            _searchRepository.Setup(r => r.Search(request, userid)).ReturnsAsync(new SearchResult
            {
                totalcount = 0,
                items = new List<UserRequest>()
            });

            var service = CreateService();

            var response = await service.Search(userid, request);

            Assert.True(response.issuccessfull);
            Assert.Empty(response.body!.results);
            Assert.Equal(2, response.body!.page);
            Assert.Equal(10, response.body!.pagesize);
            _searchRepository.Verify(r => r.Search(It.Is<SearchRequest>(req => req.radius_km == 25), userid), Times.Once);
        }

        [Fact]
        public async Task Search_WhenRepositoryReturnsNull_ReturnsEmptyResultsRatherThanThrowing()
        {
            var userid = Guid.NewGuid();
            var request = new SearchRequest { query = "engineer", page = 3, pagesize = 15 };

            _searchRepository.Setup(r => r.Search(request, userid)).ReturnsAsync((SearchResult)null!);

            var service = CreateService();

            var response = await service.Search(userid, request);

            Assert.True(response.issuccessfull);
            Assert.NotNull(response.body);
            Assert.Empty(response.body!.results);
            Assert.Equal(0, response.body!.total_count);
            Assert.Equal(3, response.body!.page);
            Assert.Equal(15, response.body!.pagesize);
        }
    }
}
