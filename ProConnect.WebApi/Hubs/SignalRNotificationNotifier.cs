using Microsoft.AspNetCore.SignalR;
using ProConnect.Application.Interface;
using ProConnect.Domain.Response;
using System;
using System.Threading.Tasks;

namespace ProConnect.WebApi.Hubs
{
    /// <summary>
    /// WebApi-layer implementation of INotificationNotifier (ProConnect.Application.Interface) - wraps
    /// IHubContext&lt;NotificationHub&gt; so NotificationService can push real-time events without the
    /// Application layer referencing SignalR or WebApi directly. See INotificationNotifier's doc comment for
    /// the full seam rationale (same pattern as SignalRMessageNotifier/IMessageNotifier).
    /// </summary>
    public class SignalRNotificationNotifier : INotificationNotifier
    {
        private readonly IHubContext<NotificationHub> _hubContext;

        public SignalRNotificationNotifier(IHubContext<NotificationHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public Task NotifyNewNotification(Guid userid, NotificationResponse notification) =>
            _hubContext.Clients.Group(NotificationHub.GroupName(userid)).SendAsync("NewNotification", notification);

        public Task NotifyUnreadCountChanged(Guid userid, int unread_count) =>
            _hubContext.Clients.Group(NotificationHub.GroupName(userid)).SendAsync("UnreadCountChanged", new
            {
                unread_count
            });
    }
}
