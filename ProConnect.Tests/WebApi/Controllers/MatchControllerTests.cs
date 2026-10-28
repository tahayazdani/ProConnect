using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
    /// MatchController is a thin passthrough to IMatchService - same conventions as the other controller
    /// test classes. Every action here is authenticated, so CurrentUserId (from the "userid" claim) is
    /// always forwarded as the first argument.
    /// </summary>
    public class MatchControllerTests
    {
        private readonly Mock<IMatchService> _matchService = new();
        private readonly Guid _userId = Guid.NewGuid();

        private MatchController CreateController()
        {
            var controller = new MatchController(new Mock<ILogger<MatchController>>().Object, _matchService.Object);

            var identity = new ClaimsIdentity(new[] { new Claim("userid", _userId.ToString()) });
            var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
        }

        [Fact]
        public async Task GetCriteria_PassesCurrentUserId()
        {
            var expected = new ProConnectResponse<MatchCriteriaResponse> { statuscode = 200, body = new MatchCriteriaResponse() };
            _matchService.Setup(s => s.GetCriteria(_userId)).ReturnsAsync(expected);

            var result = await CreateController().GetCriteria() as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _matchService.Verify(s => s.GetCriteria(_userId), Times.Once);
        }

        [Fact]
        public async Task UpdateCriteria_PassesCurrentUserIdAndRequest()
        {
            var request = new MatchCriteriaRequest { city = "Toronto" };
            var expected = new ProConnectResponse<MatchCriteriaResponse> { statuscode = 200, body = new MatchCriteriaResponse() };
            _matchService.Setup(s => s.UpdateCriteria(_userId, request)).ReturnsAsync(expected);

            var result = await CreateController().UpdateCriteria(request) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _matchService.Verify(s => s.UpdateCriteria(_userId, request), Times.Once);
        }

        [Fact]
        public async Task GetRecommendations_PassesCurrentUserIdAndRequest()
        {
            var request = new MatchCandidatePageRequest { page = 2 };
            var expected = new ProConnectResponse<MatchCandidatePageResponse> { statuscode = 200, body = new MatchCandidatePageResponse() };
            _matchService.Setup(s => s.GetRecommendations(_userId, request)).ReturnsAsync(expected);

            var result = await CreateController().GetRecommendations(request) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _matchService.Verify(s => s.GetRecommendations(_userId, request), Times.Once);
        }

        [Fact]
        public async Task Like_PassesCurrentUserIdAndTargetUserId()
        {
            var targetUserId = Guid.NewGuid();
            var expected = new ProConnectResponse<SwipeResponse> { statuscode = 200, body = new SwipeResponse() };
            _matchService.Setup(s => s.Like(_userId, targetUserId)).ReturnsAsync(expected);

            var result = await CreateController().Like(targetUserId) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _matchService.Verify(s => s.Like(_userId, targetUserId), Times.Once);
        }

        [Fact]
        public async Task Pass_PassesCurrentUserIdAndTargetUserId()
        {
            var targetUserId = Guid.NewGuid();
            var expected = new ProConnectResponse<SwipeResponse> { statuscode = 200, body = new SwipeResponse() };
            _matchService.Setup(s => s.Pass(_userId, targetUserId)).ReturnsAsync(expected);

            var result = await CreateController().Pass(targetUserId) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _matchService.Verify(s => s.Pass(_userId, targetUserId), Times.Once);
        }

        [Fact]
        public async Task GetMatches_PassesCurrentUserIdAndRequest()
        {
            var request = new MatchCandidatePageRequest();
            var expected = new ProConnectResponse<MatchSummaryPageResponse> { statuscode = 200, body = new MatchSummaryPageResponse() };
            _matchService.Setup(s => s.GetMatches(_userId, request)).ReturnsAsync(expected);

            var result = await CreateController().GetMatches(request) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _matchService.Verify(s => s.GetMatches(_userId, request), Times.Once);
        }

        [Fact]
        public async Task Unmatch_PassesCurrentUserIdAndMatchId()
        {
            var matchId = Guid.NewGuid();
            var expected = new ProConnectResponse<string> { statuscode = 200, body = "ok" };
            _matchService.Setup(s => s.Unmatch(_userId, matchId)).ReturnsAsync(expected);

            var result = await CreateController().Unmatch(matchId) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _matchService.Verify(s => s.Unmatch(_userId, matchId), Times.Once);
        }

        [Fact]
        public async Task Unmatch_WhenMatchNotFoundForUser_ForwardsCleanFailure()
        {
            var matchId = Guid.NewGuid();
            var expected = new ProConnectResponse<string> { statuscode = 200, issuccessfull = false, errormessage = "Match not found" };
            _matchService.Setup(s => s.Unmatch(_userId, matchId)).ReturnsAsync(expected);

            var result = await CreateController().Unmatch(matchId) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            Assert.Same(expected, result.Value);
        }

        [Fact]
        public async Task GetIncomingLikes_PassesCurrentUserIdAndRequest()
        {
            var request = new MatchCandidatePageRequest();
            var expected = new ProConnectResponse<MatchCandidatePageResponse> { statuscode = 200, body = new MatchCandidatePageResponse() };
            _matchService.Setup(s => s.GetIncomingLikes(_userId, request)).ReturnsAsync(expected);

            var result = await CreateController().GetIncomingLikes(request) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _matchService.Verify(s => s.GetIncomingLikes(_userId, request), Times.Once);
        }

        [Fact]
        public async Task GetSeenProfiles_PassesCurrentUserIdAndRequest()
        {
            var request = new MatchCandidatePageRequest();
            var expected = new ProConnectResponse<MatchCandidatePageResponse> { statuscode = 200, body = new MatchCandidatePageResponse() };
            _matchService.Setup(s => s.GetSeenProfiles(_userId, request)).ReturnsAsync(expected);

            var result = await CreateController().GetSeenProfiles(request) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _matchService.Verify(s => s.GetSeenProfiles(_userId, request), Times.Once);
        }

        [Fact]
        public async Task BlockUser_PassesCurrentUserIdAndTargetUserId()
        {
            var targetUserId = Guid.NewGuid();
            var expected = new ProConnectResponse<string> { statuscode = 200, body = "ok" };
            _matchService.Setup(s => s.BlockUser(_userId, targetUserId)).ReturnsAsync(expected);

            var result = await CreateController().BlockUser(targetUserId) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _matchService.Verify(s => s.BlockUser(_userId, targetUserId), Times.Once);
        }
    }
}
