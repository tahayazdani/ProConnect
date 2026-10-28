using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ProConnect.Application.Interface;
using ProConnect.Domain.Const;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using ProConnect.WebApi.Controllers;
using System;
using System.Security.Claims;

namespace ProConnect.Tests.WebApi.Controllers
{
    /// <summary>
    /// NotificationController is a thin passthrough to INotificationService - same conventions as the
    /// other controller test classes. Note NotificationController has no ILogger dependency (unlike most
    /// of the other controllers), see its constructor.
    /// </summary>
    public class NotificationControllerTests
    {
        private readonly Mock<INotificationService> _notificationService = new();
        private readonly Guid _userId = Guid.NewGuid();

        private NotificationController CreateController()
        {
            var controller = new NotificationController(_notificationService.Object);

            var identity = new ClaimsIdentity(new[] { new Claim("userid", _userId.ToString()) });
            var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
        }

        [Fact]
        public async Task GetNotifications_PassesCurrentUserIdAndRequest()
        {
            var request = new NotificationPageRequest { page = 2 };
            var expected = new ProConnectResponse<NotificationPageResponse> { statuscode = 200, body = new NotificationPageResponse() };
            _notificationService.Setup(s => s.GetNotifications(_userId, request)).ReturnsAsync(expected);

            var result = await CreateController().GetNotifications(request) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _notificationService.Verify(s => s.GetNotifications(_userId, request), Times.Once);
        }

        [Fact]
        public async Task GetUnreadCount_PassesCurrentUserId()
        {
            var expected = new ProConnectResponse<UnreadCountResponse> { statuscode = 200, body = new UnreadCountResponse() };
            _notificationService.Setup(s => s.GetUnreadCount(_userId)).ReturnsAsync(expected);

            var result = await CreateController().GetUnreadCount() as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _notificationService.Verify(s => s.GetUnreadCount(_userId), Times.Once);
        }

        [Fact]
        public async Task MarkAsRead_PassesCurrentUserIdAndNotificationId()
        {
            var notificationId = Guid.NewGuid();
            var expected = new ProConnectResponse<string> { statuscode = 200, body = "ok" };
            _notificationService.Setup(s => s.MarkAsRead(_userId, notificationId)).ReturnsAsync(expected);

            var result = await CreateController().MarkAsRead(notificationId) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _notificationService.Verify(s => s.MarkAsRead(_userId, notificationId), Times.Once);
        }

        [Fact]
        public async Task MarkAllAsRead_PassesCurrentUserId()
        {
            var expected = new ProConnectResponse<string> { statuscode = 200, body = "ok" };
            _notificationService.Setup(s => s.MarkAllAsRead(_userId)).ReturnsAsync(expected);

            var result = await CreateController().MarkAllAsRead() as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _notificationService.Verify(s => s.MarkAllAsRead(_userId), Times.Once);
        }

        [Fact]
        public async Task RegisterDevice_PassesCurrentUserIdAndRequest()
        {
            var request = new RegisterDeviceTokenRequest { device_token = "token-1", platform = DevicePlatform.IOS };
            var expected = new ProConnectResponse<string> { statuscode = 200, body = "ok" };
            _notificationService.Setup(s => s.RegisterDevice(_userId, request)).ReturnsAsync(expected);

            var result = await CreateController().RegisterDevice(request) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _notificationService.Verify(s => s.RegisterDevice(_userId, request), Times.Once);
        }

        [Fact]
        public async Task UnregisterDevice_PassesCurrentUserIdAndDeviceToken()
        {
            var expected = new ProConnectResponse<string> { statuscode = 200, body = "ok" };
            _notificationService.Setup(s => s.UnregisterDevice(_userId, "simple%2Fslash")).ReturnsAsync(expected);

            var result = await CreateController().UnregisterDevice("simple%2Fslash") as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _notificationService.Verify(s => s.UnregisterDevice(_userId, "simple%2Fslash"), Times.Once);
        }
    }
}
