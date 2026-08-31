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
        public async Task<IActionResult> GetRecommendations([FromQuery] MatchCandidatePageRequest request)
        {
            var response = await _matchService.GetRecommendations(CurrentUserId, request);
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

        #region Matches

        [HttpGet]
        [Route("matches")]
        public async Task<IActionResult> GetMatches([FromQuery] MatchCandidatePageRequest request)
        {
            var response = await _matchService.GetMatches(CurrentUserId, request);
            return StatusCode(response.statuscode, response);
        }

        [HttpDelete]
        [Route("matches/{matchId}")]
        public async Task<IActionResult> Unmatch(Guid matchId)
        {
            var response = await _matchService.Unmatch(CurrentUserId, matchId);
            return StatusCode(response.statuscode, response);
        }

        #endregion

        #region Incoming Likes

        [HttpGet]
        [Route("liked-me")]
        public async Task<IActionResult> GetIncomingLikes([FromQuery] MatchCandidatePageRequest request)
        {
            var response = await _matchService.GetIncomingLikes(CurrentUserId, request);
            return StatusCode(response.statuscode, response);
        }

        #endregion

        #region Seen

        [HttpGet]
        [Route("seen")]
        public async Task<IActionResult> GetSeenProfiles([FromQuery] MatchCandidatePageRequest request)
        {
            var response = await _matchService.GetSeenProfiles(CurrentUserId, request);
            return StatusCode(response.statuscode, response);
        }

        #endregion

        #region Block

        [HttpPost]
        [Route("{targetUserId}/block")]
        public async Task<IActionResult> BlockUser(Guid targetUserId)
        {
            var response = await _matchService.BlockUser(CurrentUserId, targetUserId);
            return StatusCode(response.statuscode, response);
        }

        #endregion
    }
}
