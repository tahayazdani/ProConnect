using Microsoft.AspNetCore.SignalR;
using ProConnect.Application.Interface;
using ProConnect.Domain.Response;
using System;
using System.Threading.Tasks;

namespace ProConnect.WebApi.Hubs
{
    /// <summary>
    /// WebApi-layer implementation of IMessageNotifier (ProConnect.Application.Interface) - wraps
    /// IHubContext&lt;MessageHub&gt; so MessageService can push real-time events without the Application
    /// layer referencing SignalR or WebApi directly. See IMessageNotifier's doc comment for the full
    /// seam rationale (same pattern as IProfileProvider).
    /// </summary>
    public class SignalRMessageNotifier : IMessageNotifier
    {
        private readonly IHubContext<MessageHub> _hubContext;

        public SignalRMessageNotifier(IHubContext<MessageHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public Task NotifyNewMessage(MessageResponse message) =>
            _hubContext.Clients.Group(MessageHub.GroupName(message.matchid)).SendAsync("NewMessage", message);

        public Task NotifyMessagesRead(Guid matchId, Guid readerUserId, int readCount) =>
            _hubContext.Clients.Group(MessageHub.GroupName(matchId)).SendAsync("MessagesRead", new
            {
                matchid = matchId,
                readeruserid = readerUserId,
                read_count = readCount,
                read_at_utc = DateTime.UtcNow
            });
    }
}
