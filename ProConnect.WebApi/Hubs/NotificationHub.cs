using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;

namespace ProConnect.WebApi.Hubs
{
    /// <summary>
    /// Real-time push layer for notifications - additive to NotificationController's existing REST
    /// endpoints, not a replacement. Listing/paging/marking-read still go through REST exactly as before;
    /// this hub only pushes "NewNotification"/"UnreadCountChanged" events to whichever clients are connected
    /// for a given user, so an open client sees them live instead of needing to poll GET again. Grouped
    /// per-user ("user:{userid}", every device/tab a user is signed into) rather than per-match the way
    /// MessageHub is, since a notification belongs to exactly one person, not a pair.
    ///
    /// Covered by the global [Authorize]-by-default fallback policy configured in Program.cs, same as
    /// MessageHub - no separate [Authorize] attribute needed here.
    /// </summary>
    public class NotificationHub : Hub
    {
        /// <summary>SignalR group name for a given user - every connection that user has open joins it.</summary>
        public static string GroupName(Guid userid) => $"user-{userid}";

        /// <summary>
        /// Auto-joins the caller's own group on connect - unlike MessageHub.JoinConversation (which needs an
        /// explicit per-match ownership check), there's nothing to authorize here beyond "you are who your
        /// JWT says you are", so this can happen immediately rather than waiting for an explicit client call.
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            var userId = CurrentUserId;
            if (userId != Guid.Empty)
                await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(userId));

            await base.OnConnectedAsync();
        }

        // Same claim name/parsing as BaseController.CurrentUserId/MessageHub.CurrentUserId, re-implemented
        // here since Hub doesn't share a base type with ControllerBase.
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
