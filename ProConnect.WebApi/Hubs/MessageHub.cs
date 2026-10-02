using Microsoft.AspNetCore.SignalR;
using ProConnect.Infrastructure.Interface;
using System;
using System.Threading.Tasks;

namespace ProConnect.WebApi.Hubs
{
    /// <summary>
    /// Real-time push layer for messaging - additive to MessageController's existing REST endpoints,
    /// not a replacement. Sending and paging history still go through REST exactly as before; this hub
    /// only pushes "NewMessage"/"MessagesRead" events to whichever clients have joined a match's group,
    /// so the other participant sees them live instead of needing to poll GET again. See
    /// PROJECT_NOTES.md's "Messaging (Phase 5) - SignalR" decision for the full design rationale.
    ///
    /// Covered by the global [Authorize]-by-default fallback policy configured in Program.cs
    /// (AuthorizationOptions.FallbackPolicy applies to all endpoints, including mapped hubs) - no
    /// separate [Authorize] attribute needed here, matching every REST controller's convention.
    /// </summary>
    public class MessageHub : Hub
    {
        private readonly IMessageRepository _messageRepository;

        public MessageHub(IMessageRepository messageRepository)
        {
            _messageRepository = messageRepository;
        }

        /// <summary>SignalR group name for a given match - one group per conversation.</summary>
        public static string GroupName(Guid matchId) => $"match-{matchId}";

        /// <summary>
        /// Client calls this right after connecting (or when opening a conversation) to start receiving
        /// NewMessage/MessagesRead pushes for that match. Same ownership check as
        /// MessageService/MessageRepository.IsParticipant - a user can't join a group for a match they
        /// aren't one of the two participants of.
        /// </summary>
        public async Task JoinConversation(Guid matchId)
        {
            var userId = CurrentUserId;
            if (userId == Guid.Empty || !await _messageRepository.IsParticipant(userId, matchId))
                throw new HubException("Match not found.");

            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(matchId));
        }

        /// <summary>Stops pushes for a match to this connection - e.g. when the client navigates away.</summary>
        public Task LeaveConversation(Guid matchId) =>
            Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(matchId));

        // Same claim name/parsing as BaseController.CurrentUserId, re-implemented here since Hub doesn't
        // share a base type with ControllerBase - Context.User is populated by the same JWT bearer
        // handler as HttpContext.User (see Program.cs's JwtBearerEvents.OnMessageReceived for how the
        // token reaches the handler on a WebSocket connection, which can't set an Authorization header).
        private Guid CurrentUserId
        {
            get
            {
                var claimValue = Context.User?.FindFirst("userid")?.Value;
                return Guid.TryParse(claimValue, out var userId) ? userId : Guid.Empty;
            }
        }
    }
}
