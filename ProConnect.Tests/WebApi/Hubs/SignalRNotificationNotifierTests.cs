using Microsoft.AspNetCore.SignalR;
using Moq;
using ProConnect.Domain.Response;
using ProConnect.WebApi.Hubs;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProConnect.Tests.WebApi.Hubs
{
    /// <summary>
    /// SignalRNotificationNotifier just routes INotificationNotifier calls onto
    /// IHubContext&lt;NotificationHub&gt;.Clients - no branching logic of its own, so these tests only verify
    /// the right group name (NotificationHub.GroupName) and the right SignalR method name/payload are used.
    /// Same IClientProxy.SendCoreAsync mocking approach as SignalRMessageNotifierTests, since SendAsync is an
    /// extension method over the real interface member.
    /// </summary>
    public class SignalRNotificationNotifierTests
    {
        private readonly Mock<IHubContext<NotificationHub>> _hubContext = new();
        private readonly Mock<IHubClients> _hubClients = new();
        private readonly Mock<IClientProxy> _clientProxy = new();

        public SignalRNotificationNotifierTests()
        {
            _hubContext.Setup(h => h.Clients).Returns(_hubClients.Object);
            _hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxy.Object);
            _clientProxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        private SignalRNotificationNotifier CreateNotifier() => new(_hubContext.Object);

        [Fact]
        public async Task NotifyNewNotification_SendsToUserGroupWithNotificationPayload()
        {
            var userId = Guid.NewGuid();
            var notification = new NotificationResponse { title = "Hi", body = "There" };
            var notifier = CreateNotifier();

            await notifier.NotifyNewNotification(userId, notification);

            _hubClients.Verify(c => c.Group(NotificationHub.GroupName(userId)), Times.Once);
            _clientProxy.Verify(p => p.SendCoreAsync(
                "NewNotification",
                It.Is<object?[]>(args => args.Length == 1 && ReferenceEquals(args[0], notification)),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task NotifyUnreadCountChanged_SendsToUserGroupWithUnreadCountPayload()
        {
            var userId = Guid.NewGuid();
            object?[]? capturedArgs = null;
            _clientProxy.Setup(p => p.SendCoreAsync("UnreadCountChanged", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Callback<string, object?[], CancellationToken>((_, args, __) => capturedArgs = args)
                .Returns(Task.CompletedTask);
            var notifier = CreateNotifier();

            await notifier.NotifyUnreadCountChanged(userId, 7);

            _hubClients.Verify(c => c.Group(NotificationHub.GroupName(userId)), Times.Once);
            Assert.NotNull(capturedArgs);
            var payload = Assert.Single(capturedArgs!);
            Assert.Equal(7, (int)payload!.GetType().GetProperty("unread_count")!.GetValue(payload)!);
        }
    }
}
