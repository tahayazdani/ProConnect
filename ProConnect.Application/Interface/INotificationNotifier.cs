using System;
using System.Threading.Tasks;
using ProConnect.Domain.Response;

namespace ProConnect.Application.Interface
{
    /// <summary>
    /// Seam between NotificationService and the real-time in-app push transport. Implemented by
    /// ProConnect.WebApi/Hubs/SignalRNotificationNotifier.cs, which wraps a SignalR
    /// IHubContext&lt;NotificationHub&gt; - same split IMessageNotifier uses for messaging, except grouped
    /// per-user (every device a user is connected from) rather than per-match.
    ///
    /// This is a live nudge for clients that are already connected (e.g. "a new item showed up, refresh the
    /// list/badge"), separate from IPushNotificationGateway's OS-level push to a registered device token -
    /// a client with the app open doesn't need an OS push, but does want this live update; a client with
    /// the app closed needs the OS push and has no open SignalR connection to receive this on. Best-effort
    /// only, same "swallow after logging" contract as IMessageNotifier - the Notification row is already
    /// the source of truth in Postgres regardless of whether this live push succeeds.
    /// </summary>
    public interface INotificationNotifier
    {
        /// <summary>Pushes a newly-created notification to every client currently connected for this user.</summary>
        Task NotifyNewNotification(Guid userid, NotificationResponse notification);

        /// <summary>Pushes an updated unread count - sent after MarkAsRead/MarkAllAsRead so an open client's
        /// badge updates live instead of waiting for its next poll.</summary>
        Task NotifyUnreadCountChanged(Guid userid, int unread_count);
    }
}
