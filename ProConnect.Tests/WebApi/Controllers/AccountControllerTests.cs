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
    /// AccountController is a thin passthrough to IUserService's own-profile actions, same conventions as
    /// UserControllerTests - verify the CurrentUserId claim is forwarded and the service's statuscode/body
    /// makes it onto the ObjectResult unchanged.
    /// </summary>
    public class AccountControllerTests
    {
        private readonly Mock<IUserService> _userService = new();
        private readonly Guid _userId = Guid.NewGuid();

        private AccountController CreateController()
        {
            var controller = new AccountController(new Mock<ILogger<AccountController>>().Object, _userService.Object);

            var identity = new ClaimsIdentity(new[] { new Claim("userid", _userId.ToString()) });
            var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
        }

        [Fact]
        public async Task GetProfile_PassesCurrentUserIdAndForwardsResponse()
        {
            var expected = new ProConnectResponse<SignUpResponse> { statuscode = 200, body = new SignUpResponse() };
            _userService.Setup(s => s.GetProfile(_userId)).ReturnsAsync(expected);

            var result = await CreateController().GetProfile() as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            Assert.Same(expected, result.Value);
            _userService.Verify(s => s.GetProfile(_userId), Times.Once);
        }

        [Fact]
        public async Task UpdateProfile_PassesCurrentUserIdAndRequest()
        {
            var request = new UpdateProfileRequest { city = "Toronto" };
            var expected = new ProConnectResponse<SignUpResponse> { statuscode = 200, body = new SignUpResponse() };
            _userService.Setup(s => s.UpdateProfile(_userId, request)).ReturnsAsync(expected);

            var result = await CreateController().UpdateProfile(request) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _userService.Verify(s => s.UpdateProfile(_userId, request), Times.Once);
        }

        [Fact]
        public async Task DeactivateAccount_PassesCurrentUserId()
        {
            var expected = new ProConnectResponse<string> { statuscode = 200, body = "ok" };
            _userService.Setup(s => s.DeactivateAccount(_userId)).ReturnsAsync(expected);

            var result = await CreateController().DeactivateAccount() as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _userService.Verify(s => s.DeactivateAccount(_userId), Times.Once);
        }

        [Fact]
        public async Task DeleteAccount_PassesCurrentUserId()
        {
            var expected = new ProConnectResponse<string> { statuscode = 200, body = "ok" };
            _userService.Setup(s => s.DeleteAccount(_userId)).ReturnsAsync(expected);

            var result = await CreateController().DeleteAccount() as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _userService.Verify(s => s.DeleteAccount(_userId), Times.Once);
        }

        [Fact]
        public void Ping_ReturnsOkWithoutCallingService()
        {
            var result = CreateController().Ping() as OkObjectResult;

            Assert.NotNull(result);
            _userService.VerifyNoOtherCalls();
        }
    }
}
