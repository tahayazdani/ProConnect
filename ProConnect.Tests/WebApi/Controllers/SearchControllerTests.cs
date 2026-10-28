using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ProConnect.Application.Interface;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using ProConnect.WebApi.Controllers;
using System;
using System.Security.Claims;

namespace ProConnect.Tests.WebApi.Controllers
{
    /// <summary>
    /// SearchController is a single-action thin passthrough to ISearchService, same conventions as the
    /// other controller test classes.
    /// </summary>
    public class SearchControllerTests
    {
        private readonly Mock<ISearchService> _searchService = new();
        private readonly Guid _userId = Guid.NewGuid();

        private SearchController CreateController()
        {
            var controller = new SearchController(_searchService.Object);

            var identity = new ClaimsIdentity(new[] { new Claim("userid", _userId.ToString()) });
            var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
        }

        [Fact]
        public async Task Search_PassesCurrentUserIdAndRequest()
        {
            var request = new SearchRequest { query = "engineer" };
            var expected = new ProConnectResponse<SearchResponse> { statuscode = 200, body = new SearchResponse() };
            _searchService.Setup(s => s.Search(_userId, request)).ReturnsAsync(expected);

            var result = await CreateController().Search(request) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _searchService.Verify(s => s.Search(_userId, request), Times.Once);
        }
    }
}
