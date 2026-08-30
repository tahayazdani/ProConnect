using Microsoft.AspNetCore.Mvc;
using ProConnect.Application.Interface;
using ProConnect.Domain.Request;

namespace ProConnect.WebApi.Controllers
{
    public class SearchController : BaseController
    {
        private readonly ISearchService _searchService;

        public SearchController(ISearchService searchService)
        {
            _searchService = searchService;
        }

        [HttpGet]
        public async Task<IActionResult> Search([FromQuery] SearchRequest request)
        {
            var response = await _searchService.Search(CurrentUserId, request);
            return StatusCode(response.statuscode, response);
        }
    }
}
