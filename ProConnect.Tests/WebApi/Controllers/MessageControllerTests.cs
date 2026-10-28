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
    /// MessageController is a thin passthrough to IMessageService - same conventions as the other
    /// controller test classes.
    /// </summary>
    public class MessageControllerTests
    {
        private readonly Mock<IMessageService> _messageService = new();
        private readonly Guid _userId = Guid.NewGuid();

        private MessageController CreateController()
        {
            var controller = new MessageController(new Mock<ILogger<MessageController>>().Object, _messageService.Object);

            var identity = new ClaimsIdentity(new[] { new Claim("userid", _userId.ToString()) });
            var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
        }

        [Fact]
        public async Task GetMessages_PassesCurrentUserIdMatchIdAndRequest()
        {
            var matchId = Guid.NewGuid();
            var request = new MessagePageRequest { page = 2 };
            var expected = new ProConnectResponse<MessagePageResponse> { statuscode = 200, body = new MessagePageResponse() };
            _messageService.Setup(s => s.GetMessages(_userId, matchId, request)).ReturnsAsync(expected);

            var result = await CreateController().GetMessages(matchId, request) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _messageService.Verify(s => s.GetMessages(_userId, matchId, request), Times.Once);
        }

        [Fact]
        public async Task GetMessages_WhenUserNotParticipant_ForwardsCleanFailure()
        {
            var matchId = Guid.NewGuid();
            var request = new MessagePageRequest();
            var expected = new ProConnectResponse<MessagePageResponse> { statuscode = 200, issuccessfull = false, errormessage = "Match not found" };
            _messageService.Setup(s => s.GetMessages(_userId, matchId, request)).ReturnsAsync(expected);

            var result = await CreateController().GetMessages(matchId, request) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            Assert.Same(expected, result.Value);
        }

        [Fact]
        public async Task SendMessage_PassesCurrentUserIdMatchIdAndRequest()
        {
            var matchId = Guid.NewGuid();
            var request = new MessageRequest { content = "hello" };
            var expected = new ProConnectResponse<MessageResponse> { statuscode = 201, body = new MessageResponse() };
            _messageService.Setup(s => s.SendMessage(_userId, matchId, request)).ReturnsAsync(expected);

            var result = await CreateController().SendMessage(matchId, request) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(201, result.StatusCode);
            _messageService.Verify(s => s.SendMessage(_userId, matchId, request), Times.Once);
        }
    }
}
