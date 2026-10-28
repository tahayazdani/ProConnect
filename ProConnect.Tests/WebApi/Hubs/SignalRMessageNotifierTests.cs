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
    /// SignalRMessageNotifier just routes IMessageNotifier calls onto IHubContext&lt;MessageHub&gt;.Clients -
    /// no branching logic of its own, so these tests only verify the right group name (MessageHub.GroupName)
    /// and the right SignalR method name/payload are used. IClientProxy.SendAsync is an extension method over
    /// SendCoreAsync, so the mock targets SendCoreAsync (the actual interface member) directly.
    /// </summary>
    public class SignalRMessageNotifierTests
    {
        private readonly Mock<IHubContext<MessageHub>> _hubContext = new();
        private readonly Mock<IHubClients> _hubClients = new();
        private readonly Mock<IClientProxy> _clientProxy = new();

        public SignalRMessageNotifierTests()
        {
            _hubContext.Setup(h => h.Clients).Returns(_hubClients.Object);
            _hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxy.Object);
            _clientProxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        private SignalRMessageNotifier CreateNotifier() => new(_hubContext.Object);

        [Fact]
        public async Task NotifyNewMessage_SendsToMatchGroupWithMessagePayload()
        {
            var matchId = Guid.NewGuid();
            var message = new MessageResponse { matchid = matchId, content = "hi" };
            var notifier = CreateNotifier();

            await notifier.NotifyNewMessage(message);

            _hubClients.Verify(c => c.Group(MessageHub.GroupName(matchId)), Times.Once);
            _clientProxy.Verify(p => p.SendCoreAsync(
                "NewMessage",
                It.Is<object?[]>(args => args.Length == 1 && ReferenceEquals(args[0], message)),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task NotifyMessagesRead_SendsToMatchGroupWithExpectedPayloadShape()
        {
            var matchId = Guid.NewGuid();
            var readerUserId = Guid.NewGuid();
            object?[]? capturedArgs = null;
            _clientProxy.Setup(p => p.SendCoreAsync("MessagesRead", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Callback<string, object?[], CancellationToken>((_, args, __) => capturedArgs = args)
                .Returns(Task.CompletedTask);
            var notifier = CreateNotifier();

            await notifier.NotifyMessagesRead(matchId, readerUserId, 3);

            _hubClients.Verify(c => c.Group(MessageHub.GroupName(matchId)), Times.Once);
            Assert.NotNull(capturedArgs);
            var payload = Assert.Single(capturedArgs!);
            Assert.Equal(matchId, (Guid)payload!.GetType().GetProperty("matchid")!.GetValue(payload)!);
            Assert.Equal(readerUserId, (Guid)payload.GetType().GetProperty("readeruserid")!.GetValue(payload)!);
            Assert.Equal(3, (int)payload.GetType().GetProperty("read_count")!.GetValue(payload)!);
        }
    }
}
