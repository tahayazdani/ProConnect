using Microsoft.Extensions.Logging;
using Moq;
using ProConnect.Application.Interface;
using ProConnect.Application.Service;
using ProConnect.Domain.Const;
using ProConnect.Domain.Model;
using ProConnect.Domain.Request;
using ProConnect.Infrastructure.Interface;

namespace ProConnect.Tests.Application.Service
{
    /// <summary>
    /// Covers NotificationService.Notify's persist-then-best-effort-fan-out shape (row always persisted,
    /// push/email/live-notify each independently swallow-and-log) plus light coverage of the remaining
    /// public methods (GetUnreadCount, MarkAsRead, RegisterDevice/UnregisterDevice's validation-failure
    /// branches). GetNotifications/MarkAllAsRead and RegisterDevice's happy-path audit-log branch aren't
    /// covered here - they're thin repository pass-throughs with no branching of their own beyond what's
    /// already exercised below, left for a follow-up if deeper coverage is wanted.
    /// </summary>
    public class NotificationServiceTests
    {
        private readonly Mock<INotificationRepository> _notificationRepository = new();
        private readonly Mock<IPushNotificationGateway> _pushNotificationGateway = new();
        private readonly Mock<INotificationNotifier> _notificationNotifier = new();
        private readonly Mock<IEmailGateway> _emailGateway = new();
        private readonly Mock<IEmailTemplateRenderer> _emailTemplateRenderer = new();
        private readonly Mock<IUserRepository> _userRepository = new();

        private NotificationService CreateService()
        {
            return new NotificationService(
                _notificationRepository.Object,
                _pushNotificationGateway.Object,
                _notificationNotifier.Object,
                _emailGateway.Object,
                _emailTemplateRenderer.Object,
                _userRepository.Object,
                new Mock<ILogger<NotificationService>>().Object);
        }

        private void SetupCreateNotification(Guid userid, NotificationType type, string title, string body)
        {
            _notificationRepository.Setup(r => r.CreateNotification(It.IsAny<Notification>()))
                .ReturnsAsync((Notification n) => new Notification
                {
                    notificationid = Guid.NewGuid(),
                    userid = n.userid,
                    type = n.type,
                    title = n.title,
                    body = n.body,
                    related_entity_id = n.related_entity_id,
                    is_read = n.is_read,
                    created_at_utc = DateTime.UtcNow
                });
        }

        [Fact]
        public async Task Notify_AlwaysPersistsNotificationRow()
        {
            var userid = Guid.NewGuid();
            SetupCreateNotification(userid, NotificationType.NewMatch, "title", "body");
            _notificationRepository.Setup(r => r.IsChannelEnabled(It.IsAny<NotificationType>(), It.IsAny<NotificationChannel>())).ReturnsAsync(false);

            var service = CreateService();

            await service.Notify(userid, NotificationType.NewMatch, "title", "body");

            _notificationRepository.Verify(r => r.CreateNotification(It.Is<Notification>(n =>
                n.userid == userid && n.type == NotificationType.NewMatch && n.title == "title" && n.body == "body")), Times.Once);
        }

        [Fact]
        public async Task Notify_WhenCreateNotificationThrows_SwallowsExceptionAndSkipsDownstreamDispatch()
        {
            var userid = Guid.NewGuid();
            _notificationRepository.Setup(r => r.CreateNotification(It.IsAny<Notification>())).ThrowsAsync(new Exception("db unavailable"));

            var service = CreateService();

            await service.Notify(userid, NotificationType.NewMatch, "title", "body");

            _pushNotificationGateway.Verify(p => p.SendPushAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()), Times.Never);
            _emailGateway.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _notificationNotifier.Verify(n => n.NotifyNewNotification(It.IsAny<Guid>(), It.IsAny<ProConnect.Domain.Response.NotificationResponse>()), Times.Never);
        }

        [Fact]
        public async Task Notify_WhenPushChannelDisabled_NeverSendsPush()
        {
            var userid = Guid.NewGuid();
            SetupCreateNotification(userid, NotificationType.NewMatch, "title", "body");
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.NewMatch, NotificationChannel.Push)).ReturnsAsync(false);
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.NewMatch, NotificationChannel.Email)).ReturnsAsync(false);

            var service = CreateService();

            await service.Notify(userid, NotificationType.NewMatch, "title", "body");

            _notificationRepository.Verify(r => r.GetDeviceTokens(It.IsAny<Guid>()), Times.Never);
            _pushNotificationGateway.Verify(p => p.SendPushAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()), Times.Never);
        }

        [Fact]
        public async Task Notify_WhenPushEnabledButNoDeviceTokens_NeverSendsPush()
        {
            var userid = Guid.NewGuid();
            SetupCreateNotification(userid, NotificationType.NewMatch, "title", "body");
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.NewMatch, NotificationChannel.Push)).ReturnsAsync(true);
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.NewMatch, NotificationChannel.Email)).ReturnsAsync(false);
            _notificationRepository.Setup(r => r.GetDeviceTokens(userid)).ReturnsAsync(new List<UserDeviceToken>());

            var service = CreateService();

            await service.Notify(userid, NotificationType.NewMatch, "title", "body");

            _pushNotificationGateway.Verify(p => p.SendPushAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()), Times.Never);
        }

        [Fact]
        public async Task Notify_WhenPushEnabledAndDeviceTokensExist_SendsPushWithTokensTitleAndBody()
        {
            var userid = Guid.NewGuid();
            SetupCreateNotification(userid, NotificationType.NewMatch, "You have a new match!", "Check it out.");
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.NewMatch, NotificationChannel.Push)).ReturnsAsync(true);
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.NewMatch, NotificationChannel.Email)).ReturnsAsync(false);
            var tokens = new List<UserDeviceToken>
            {
                new UserDeviceToken { device_token = "token-1" },
                new UserDeviceToken { device_token = "token-2" }
            };
            _notificationRepository.Setup(r => r.GetDeviceTokens(userid)).ReturnsAsync(tokens);

            var service = CreateService();

            await service.Notify(userid, NotificationType.NewMatch, "You have a new match!", "Check it out.");

            _pushNotificationGateway.Verify(p => p.SendPushAsync(
                It.Is<IEnumerable<string>>(t => t.Contains("token-1") && t.Contains("token-2")),
                "You have a new match!",
                "Check it out.",
                It.IsAny<IDictionary<string, string>>()), Times.Once);
        }

        [Fact]
        public async Task Notify_WhenSendPushAsyncThrows_CompletesSuccessfullyWithoutPropagating()
        {
            var userid = Guid.NewGuid();
            SetupCreateNotification(userid, NotificationType.NewMatch, "title", "body");
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.NewMatch, NotificationChannel.Push)).ReturnsAsync(true);
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.NewMatch, NotificationChannel.Email)).ReturnsAsync(false);
            _notificationRepository.Setup(r => r.GetDeviceTokens(userid)).ReturnsAsync(new List<UserDeviceToken> { new UserDeviceToken { device_token = "token-1" } });
            _pushNotificationGateway.Setup(p => p.SendPushAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
                .ThrowsAsync(new Exception("fcm unavailable"));

            var service = CreateService();

            var exception = await Record.ExceptionAsync(() => service.Notify(userid, NotificationType.NewMatch, "title", "body"));

            Assert.Null(exception);
            _notificationRepository.Verify(r => r.CreateNotification(It.IsAny<Notification>()), Times.Once);
        }

        [Fact]
        public async Task Notify_WhenEmailChannelDisabled_NeverLooksUpUserOrSendsEmail()
        {
            var userid = Guid.NewGuid();
            SetupCreateNotification(userid, NotificationType.Welcome, "title", "body");
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.Welcome, NotificationChannel.Push)).ReturnsAsync(false);
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.Welcome, NotificationChannel.Email)).ReturnsAsync(false);

            var service = CreateService();

            await service.Notify(userid, NotificationType.Welcome, "title", "body");

            _userRepository.Verify(u => u.GetById(It.IsAny<Guid>()), Times.Never);
            _emailGateway.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Notify_WhenEmailEnabledButUserHasNoEmailAddress_NeverSendsEmail()
        {
            var userid = Guid.NewGuid();
            SetupCreateNotification(userid, NotificationType.Welcome, "title", "body");
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.Welcome, NotificationChannel.Push)).ReturnsAsync(false);
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.Welcome, NotificationChannel.Email)).ReturnsAsync(true);
            _userRepository.Setup(u => u.GetById(userid)).ReturnsAsync(new UserRequest { userid = userid, emailaddress = null! });

            var service = CreateService();

            await service.Notify(userid, NotificationType.Welcome, "title", "body");

            _emailGateway.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Notify_WhenEmailEnabledButGetByIdReturnsNull_NeverSendsEmail()
        {
            var userid = Guid.NewGuid();
            SetupCreateNotification(userid, NotificationType.Welcome, "title", "body");
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.Welcome, NotificationChannel.Push)).ReturnsAsync(false);
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.Welcome, NotificationChannel.Email)).ReturnsAsync(true);
            _userRepository.Setup(u => u.GetById(userid)).ReturnsAsync((UserRequest)null!);

            var service = CreateService();

            await service.Notify(userid, NotificationType.Welcome, "title", "body");

            _emailGateway.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Notify_WhenEmailEnabledAndUserHasEmailButRenderReturnsNull_NeverSendsEmail()
        {
            var userid = Guid.NewGuid();
            SetupCreateNotification(userid, NotificationType.NewMatch, "title", "body");
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.NewMatch, NotificationChannel.Push)).ReturnsAsync(false);
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.NewMatch, NotificationChannel.Email)).ReturnsAsync(true);
            _userRepository.Setup(u => u.GetById(userid)).ReturnsAsync(new UserRequest { userid = userid, emailaddress = "jdoe@example.com", first_name = "Jane" });
            _emailTemplateRenderer.Setup(t => t.Render(NotificationType.NewMatch, It.IsAny<IDictionary<string, string>>())).Returns((RenderedEmailTemplate)null!);

            var service = CreateService();

            await service.Notify(userid, NotificationType.NewMatch, "title", "body");

            _emailGateway.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Notify_WhenEmailEnabledUserHasEmailAndRenderSucceeds_SendsEmailWithRenderedContent()
        {
            var userid = Guid.NewGuid();
            SetupCreateNotification(userid, NotificationType.Welcome, "title", "body");
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.Welcome, NotificationChannel.Push)).ReturnsAsync(false);
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.Welcome, NotificationChannel.Email)).ReturnsAsync(true);
            _userRepository.Setup(u => u.GetById(userid)).ReturnsAsync(new UserRequest { userid = userid, emailaddress = "jdoe@example.com", first_name = "Jane" });
            var rendered = new RenderedEmailTemplate("Welcome to ProConnect, Jane!", "<p>Hi Jane</p>");
            _emailTemplateRenderer.Setup(t => t.Render(NotificationType.Welcome, It.IsAny<IDictionary<string, string>>())).Returns(rendered);

            var service = CreateService();

            await service.Notify(userid, NotificationType.Welcome, "title", "body");

            _emailGateway.Verify(e => e.SendEmailAsync("jdoe@example.com", "Welcome to ProConnect, Jane!", "<p>Hi Jane</p>"), Times.Once);
        }

        [Fact]
        public async Task Notify_WhenSendEmailAsyncThrows_CompletesSuccessfullyWithoutPropagating()
        {
            var userid = Guid.NewGuid();
            SetupCreateNotification(userid, NotificationType.Welcome, "title", "body");
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.Welcome, NotificationChannel.Push)).ReturnsAsync(false);
            _notificationRepository.Setup(r => r.IsChannelEnabled(NotificationType.Welcome, NotificationChannel.Email)).ReturnsAsync(true);
            _userRepository.Setup(u => u.GetById(userid)).ReturnsAsync(new UserRequest { userid = userid, emailaddress = "jdoe@example.com", first_name = "Jane" });
            var rendered = new RenderedEmailTemplate("subject", "body");
            _emailTemplateRenderer.Setup(t => t.Render(NotificationType.Welcome, It.IsAny<IDictionary<string, string>>())).Returns(rendered);
            _emailGateway.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ThrowsAsync(new Exception("smtp unavailable"));

            var service = CreateService();

            var exception = await Record.ExceptionAsync(() => service.Notify(userid, NotificationType.Welcome, "title", "body"));

            Assert.Null(exception);
        }

        [Fact]
        public async Task GetUnreadCount_ReturnsCountFromRepository()
        {
            var userid = Guid.NewGuid();
            _notificationRepository.Setup(r => r.CountUnread(userid)).ReturnsAsync(7);

            var service = CreateService();

            var response = await service.GetUnreadCount(userid);

            Assert.True(response.issuccessfull);
            Assert.Equal(7, response.body!.unread_count);
        }

        [Fact]
        public async Task MarkAsRead_WhenFound_ReturnsSuccessAndNotifiesUnreadCountChanged()
        {
            var userid = Guid.NewGuid();
            var notificationid = Guid.NewGuid();
            _notificationRepository.Setup(r => r.MarkAsRead(userid, notificationid)).ReturnsAsync(true);
            _notificationRepository.Setup(r => r.CountUnread(userid)).ReturnsAsync(2);

            var service = CreateService();

            var response = await service.MarkAsRead(userid, notificationid);

            Assert.True(response.issuccessfull);
            Assert.Equal("ok", response.body);
            _notificationNotifier.Verify(n => n.NotifyUnreadCountChanged(userid, 2), Times.Once);
        }

        [Fact]
        public async Task MarkAsRead_WhenNotFound_ReturnsFailureAndDoesNotNotify()
        {
            var userid = Guid.NewGuid();
            var notificationid = Guid.NewGuid();
            _notificationRepository.Setup(r => r.MarkAsRead(userid, notificationid)).ReturnsAsync(false);

            var service = CreateService();

            var response = await service.MarkAsRead(userid, notificationid);

            Assert.False(response.issuccessfull);
            Assert.Equal("Notification not found.", response.errormessage);
            _notificationNotifier.Verify(n => n.NotifyUnreadCountChanged(It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task RegisterDevice_WithEmptyDeviceToken_ReturnsFailureWithoutUpserting(string? deviceToken)
        {
            var userid = Guid.NewGuid();
            var request = new RegisterDeviceTokenRequest { device_token = deviceToken! };

            var service = CreateService();

            var response = await service.RegisterDevice(userid, request);

            Assert.False(response.issuccessfull);
            Assert.Equal("A valid device_token is required.", response.errormessage);
            _notificationRepository.Verify(r => r.UpsertDeviceToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DevicePlatform>()), Times.Never);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task UnregisterDevice_WithEmptyDeviceToken_ReturnsFailureWithoutRemoving(string? deviceToken)
        {
            var userid = Guid.NewGuid();

            var service = CreateService();

            var response = await service.UnregisterDevice(userid, deviceToken!);

            Assert.False(response.issuccessfull);
            Assert.Equal("A valid device_token is required.", response.errormessage);
            _notificationRepository.Verify(r => r.RemoveDeviceToken(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
        }
    }
}
