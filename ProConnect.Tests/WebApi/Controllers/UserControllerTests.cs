using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using ProConnect.Application.Interface;
using ProConnect.Domain.Config;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using ProConnect.WebApi.Controllers;
using System;
using System.Security.Claims;

namespace ProConnect.Tests.WebApi.Controllers
{
    /// <summary>
    /// UserController is a thin passthrough to IUserService - every action just forwards to the service
    /// and relays whatever statuscode/body it returned via StatusCode(...). These tests verify (a) that
    /// forwarding (the mocked statuscode on the response makes it onto the ObjectResult unchanged), and
    /// (b) that the controller passes the right arguments to the service, in particular CurrentUserId
    /// (read off a "userid" claim on HttpContext.User, see BaseController) for the authenticated actions.
    /// </summary>
    public class UserControllerTests
    {
        private readonly Mock<IUserService> _userService = new();

        private UserController CreateController(Guid? userId = null)
        {
            var controller = new UserController(
                new Mock<ILogger<UserController>>().Object,
                _userService.Object,
                new ProConnectConfig());

            var httpContext = new DefaultHttpContext();
            if (userId.HasValue)
            {
                var identity = new ClaimsIdentity(new[] { new Claim("userid", userId.Value.ToString()) });
                httpContext.User = new ClaimsPrincipal(identity);
            }

            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
        }

        [Fact]
        public async Task GenerateConsentLinkedInLogInPage_ForwardsServiceResponse()
        {
            var expected = new ProConnectResponse<Content> { statuscode = 200, body = new Content() };
            _userService.Setup(s => s.GenerateConsentLinkedInLogInPage()).ReturnsAsync(expected);

            var result = await CreateController().GenerateConsentLinkedInLogInPage() as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            Assert.Same(expected, result.Value);
        }

        [Fact]
        public async Task LinkedInAuthentication_ForwardsCodeAndStateToService()
        {
            var expected = new ProConnectResponse<SignInResponse> { statuscode = 200, body = new SignInResponse() };
            _userService.Setup(s => s.LinkedInAuthentication("auth-code", "state-value")).ReturnsAsync(expected);

            var result = await CreateController().LinkedInAuthentication("auth-code", "state-value") as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            Assert.Same(expected, result.Value);
            _userService.Verify(s => s.LinkedInAuthentication("auth-code", "state-value"), Times.Once);
        }

        [Fact]
        public async Task SystemAuthentication_PassesCurrentUserIdFromClaim()
        {
            var userId = Guid.NewGuid();
            var expected = new ProConnectResponse<SignInResponse> { statuscode = 200, body = new SignInResponse() };
            _userService.Setup(s => s.SystemAuthentication(userId)).ReturnsAsync(expected);

            var result = await CreateController(userId).SystemAuthentication() as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _userService.Verify(s => s.SystemAuthentication(userId), Times.Once);
        }

        [Fact]
        public async Task SystemAuthentication_WithNoUserIdClaim_UsesEmptyGuid()
        {
            var expected = new ProConnectResponse<SignInResponse> { statuscode = 401, issuccessfull = false };
            _userService.Setup(s => s.SystemAuthentication(Guid.Empty)).ReturnsAsync(expected);

            var result = await CreateController().SystemAuthentication() as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(401, result.StatusCode);
            _userService.Verify(s => s.SystemAuthentication(Guid.Empty), Times.Once);
        }

        [Fact]
        public async Task RefreshToken_ForwardsRefreshTokenValueFromBody()
        {
            var expected = new ProConnectResponse<RefreshTokenResponse> { statuscode = 200, body = new RefreshTokenResponse() };
            _userService.Setup(s => s.RefreshToken("the-refresh-token")).ReturnsAsync(expected);

            var result = await CreateController().RefreshToken(new RefreshTokenRequest { refreshtoken = "the-refresh-token" }) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _userService.Verify(s => s.RefreshToken("the-refresh-token"), Times.Once);
        }

        [Fact]
        public async Task RefreshToken_WithNullBody_PassesNullThrough()
        {
            var expected = new ProConnectResponse<RefreshTokenResponse> { statuscode = 400, issuccessfull = false };
            _userService.Setup(s => s.RefreshToken(null)).ReturnsAsync(expected);

            var result = await CreateController().RefreshToken(null) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(400, result.StatusCode);
            _userService.Verify(s => s.RefreshToken(null), Times.Once);
        }

        [Fact]
        public async Task Logout_ForwardsRefreshTokenValueFromBody()
        {
            var expected = new ProConnectResponse<string> { statuscode = 200, body = "ok" };
            _userService.Setup(s => s.Logout("the-refresh-token")).ReturnsAsync(expected);

            var result = await CreateController().Logout(new RefreshTokenRequest { refreshtoken = "the-refresh-token" }) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _userService.Verify(s => s.Logout("the-refresh-token"), Times.Once);
        }

        [Fact]
        public void LinkedInSignUp_ReturnsRedirectUrlDirectlyWithoutCallingService()
        {
            var result = CreateController().LinkedInSignUp() as OkObjectResult;

            Assert.NotNull(result);
            Assert.Equal("https://www.linkedin.com/signup", result.Value);
            _userService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SignUp_ForwardsRequestToService()
        {
            var request = new SignUpRequest { email = "a@b.com", sub = "sub-1" };
            var expected = new ProConnectResponse<SignUpResponse> { statuscode = 201, body = new SignUpResponse() };
            _userService.Setup(s => s.SignUp(request)).ReturnsAsync(expected);

            var result = await CreateController().SignUp(request) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(201, result.StatusCode);
            _userService.Verify(s => s.SignUp(request), Times.Once);
        }

        [Fact]
        public async Task GetLinkedInProfile_ForwardsPublicIdentifierToService()
        {
            var expected = new ProConnectResponse<PersonProfileResponse> { statuscode = 200, body = new PersonProfileResponse() };
            _userService.Setup(s => s.GetLinkedInProfile("johndoe")).ReturnsAsync(expected);

            var result = await CreateController().GetLinkedInProfile("johndoe") as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _userService.Verify(s => s.GetLinkedInProfile("johndoe"), Times.Once);
        }

        [Fact]
        public void Ping_ReturnsOkWithCorrelationTokenWithoutCallingService()
        {
            var result = CreateController().Ping() as OkObjectResult;

            Assert.NotNull(result);
            _userService.VerifyNoOtherCalls();
        }
    }
}
