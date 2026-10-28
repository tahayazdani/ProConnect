using Microsoft.Extensions.Logging;
using Moq;
using ProConnect.Application.Interface;
using ProConnect.Application.Service;
using ProConnect.Domain.Model;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using ProConnect.Infrastructure.Interface;
using System.Collections.Generic;

namespace ProConnect.Tests.Application.Service
{
    /// <summary>
    /// Covers MessageService's two public methods end to end against mocked IMessageRepository/
    /// IMessageNotifier - participant authorization (via SendMessageIfParticipant's null-return/
    /// IsParticipant's false-return conventions), paging math, and the mark-as-read side effect of
    /// GetMessages. IMessageNotifier failures are deliberately swallowed by the service (see its "best
    /// effort" doc comment) so a notifier throw is covered too, to confirm it never bubbles up.
    /// </summary>
    public class MessageServiceTests
    {
        private readonly Mock<IMessageRepository> _messageRepository = new();
        private readonly Mock<IMessageNotifier> _messageNotifier = new();

        private MessageService CreateService()
        {
            return new MessageService(
                _messageRepository.Object,
                _messageNotifier.Object,
                new Mock<ILogger<MessageService>>().Object);
        }

        [Fact]
        public async Task SendMessage_WithEmptyContent_ReturnsFailureWithoutTouchingRepository()
        {
            var service = CreateService();
            var request = new MessageRequest { content = "   " };

            var response = await service.SendMessage(Guid.NewGuid(), Guid.NewGuid(), request);

            Assert.False(response.issuccessfull);
            Assert.Equal("content is required.", response.errormessage);
            _messageRepository.Verify(r => r.SendMessageIfParticipant(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SendMessage_WhenCallerIsAParticipant_PersistsAndPushesNotification()
        {
            var userid = Guid.NewGuid();
            var matchId = Guid.NewGuid();
            var message = new Message
            {
                messageid = Guid.NewGuid(),
                matchid = matchId,
                senderuserid = userid,
                content = "hello",
                sent_at_utc = DateTime.UtcNow,
                is_read = false
            };
            _messageRepository.Setup(r => r.SendMessageIfParticipant(matchId, userid, "hello")).ReturnsAsync(message);

            var service = CreateService();

            var response = await service.SendMessage(userid, matchId, new MessageRequest { content = "hello" });

            Assert.True(response.issuccessfull);
            Assert.NotNull(response.body);
            Assert.Equal("hello", response.body!.content);
            Assert.Equal(message.messageid, response.body!.messageid);
            _messageNotifier.Verify(n => n.NotifyNewMessage(It.Is<MessageResponse>(m => m.messageid == message.messageid)), Times.Once);
        }

        [Fact]
        public async Task SendMessage_WhenCallerIsNotAParticipant_ReturnsMatchNotFoundAndDoesNotNotify()
        {
            var userid = Guid.NewGuid();
            var matchId = Guid.NewGuid();
            _messageRepository.Setup(r => r.SendMessageIfParticipant(matchId, userid, "hello")).ReturnsAsync((Message)null!);

            var service = CreateService();

            var response = await service.SendMessage(userid, matchId, new MessageRequest { content = "hello" });

            Assert.False(response.issuccessfull);
            Assert.Equal("Match not found.", response.errormessage);
            _messageNotifier.Verify(n => n.NotifyNewMessage(It.IsAny<MessageResponse>()), Times.Never);
        }

        [Fact]
        public async Task SendMessage_WhenNotifierThrows_StillReturnsSuccess()
        {
            var userid = Guid.NewGuid();
            var matchId = Guid.NewGuid();
            var message = new Message { messageid = Guid.NewGuid(), matchid = matchId, senderuserid = userid, content = "hi" };
            _messageRepository.Setup(r => r.SendMessageIfParticipant(matchId, userid, "hi")).ReturnsAsync(message);
            _messageNotifier.Setup(n => n.NotifyNewMessage(It.IsAny<MessageResponse>())).ThrowsAsync(new Exception("signalr down"));

            var service = CreateService();

            var response = await service.SendMessage(userid, matchId, new MessageRequest { content = "hi" });

            Assert.True(response.issuccessfull);
        }

        [Fact]
        public async Task GetMessages_WhenCallerIsNotAParticipant_ReturnsMatchNotFound()
        {
            var userid = Guid.NewGuid();
            var matchId = Guid.NewGuid();
            _messageRepository.Setup(r => r.IsParticipant(userid, matchId)).ReturnsAsync(false);

            var service = CreateService();

            var response = await service.GetMessages(userid, matchId, new MessagePageRequest());

            Assert.False(response.issuccessfull);
            Assert.Equal("Match not found.", response.errormessage);
            _messageRepository.Verify(r => r.MarkAsRead(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task GetMessages_WhenCallerIsAParticipant_MarksAsReadAndReturnsPagedResults()
        {
            var userid = Guid.NewGuid();
            var matchId = Guid.NewGuid();
            var request = new MessagePageRequest { page = 2, pagesize = 10 };

            _messageRepository.Setup(r => r.IsParticipant(userid, matchId)).ReturnsAsync(true);
            _messageRepository.Setup(r => r.MarkAsRead(matchId, userid)).ReturnsAsync(3);
            _messageRepository.Setup(r => r.GetMessages(matchId, 10, 10)).ReturnsAsync(new List<Message>
            {
                new Message { messageid = Guid.NewGuid(), matchid = matchId, senderuserid = userid, content = "a" }
            });
            _messageRepository.Setup(r => r.CountMessages(matchId)).ReturnsAsync(25);

            var service = CreateService();

            var response = await service.GetMessages(userid, matchId, request);

            Assert.True(response.issuccessfull);
            Assert.NotNull(response.body);
            Assert.Equal(1, response.body!.results.Count);
            Assert.Equal(25, response.body!.total_count);
            Assert.True(response.body!.has_more);
            _messageNotifier.Verify(n => n.NotifyMessagesRead(matchId, userid, 3), Times.Once);
        }

        [Fact]
        public async Task GetMessages_WhenNothingNewToMarkAsRead_DoesNotPushReadNotification()
        {
            var userid = Guid.NewGuid();
            var matchId = Guid.NewGuid();

            _messageRepository.Setup(r => r.IsParticipant(userid, matchId)).ReturnsAsync(true);
            _messageRepository.Setup(r => r.MarkAsRead(matchId, userid)).ReturnsAsync(0);
            _messageRepository.Setup(r => r.GetMessages(matchId, 0, 50)).ReturnsAsync(new List<Message>());
            _messageRepository.Setup(r => r.CountMessages(matchId)).ReturnsAsync(0);

            var service = CreateService();

            var response = await service.GetMessages(userid, matchId, new MessagePageRequest());

            Assert.True(response.issuccessfull);
            Assert.False(response.body!.has_more);
            _messageNotifier.Verify(n => n.NotifyMessagesRead(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
        }
    }
}
