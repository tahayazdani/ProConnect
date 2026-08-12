using Microsoft.AspNetCore.Mvc;
using ProConnect.Application.Interface;
using ProConnect.Domain.Request;
using System;

namespace ProConnect.WebApi.Controllers
{
    public class MatchController : BaseController
    {
        protected readonly ILogger<MatchController> _logger;
        private readonly IMatchService _matchService;

        public MatchController(ILogger<MatchController> logger, IMatchService matchService)
        {
            _logger = logger;
            _matchService = matchService;
        }

        #region Criteria

        [HttpGet]
        [Route("criteria")]
        public async Task<IActionResult> GetCriteria()
        {
            var response = await _matchService.GetCriteria(CurrentUserId);
            return StatusCode(response.statuscode, response);
        }

        [HttpPut]
        [Route("criteria")]
        public async Task<IActionResult> UpdateCriteria(MatchCriteriaRequest request)
        {
            var response = await _matchService.UpdateCriteria(CurrentUserId, request);
            return StatusCode(response.statuscode, response);
        }

        #endregion

        #region Recommendations

        [HttpGet]
        [Route("recommendations")]
        public async Task<IActionResult> GetRecommendations()
        {
            var response = await _matchService.GetRecommendations(CurrentUserId);
            return StatusCode(response.statuscode, response);
        }

        #endregion

        #region Swipe

        [HttpPost]
        [Route("{targetUserId}/like")]
        public async Task<IActionResult> Like(Guid targetUserId)
        {
            var response = await _matchService.Like(CurrentUserId, targetUserId);
            return StatusCode(response.statuscode, response);
        }

        [HttpPost]
        [Route("{targetUserId}/pass")]
        public async Task<IActionResult> Pass(Guid targetUserId)
        {
            var response = await _matchService.Pass(CurrentUserId, targetUserId);
            return StatusCode(response.statuscode, response);
        }

        #endregion
    }
}
