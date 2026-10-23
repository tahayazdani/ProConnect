using System;
using System.Threading.Tasks;
using ProConnect.Domain.Const;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;

namespace ProConnect.Application.Interface
{
    /// <summary>
    /// Client-facing notification surface - NotificationController's whole backing service, plus the single
    /// entry point (Notify) that MatchService/SubscriptionService call to raise a notification in the first
    /// place. Everything push-provider-specific is behind IPushNotificationGateway; everything live-in-app-
    /// update-specific is behind INotificationNotifier - this service only decides *what* gets persisted and
    /// *whether* each channel fires (via NotificationChannelConfigEntity), never how either transport works.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// Raises a notification for `userid`: always persists a Notification row (the in-app/audit-trail
        /// channel), then - best-effort, failures logged and swallowed, never thrown back to the caller -
        /// pushes over whichever of Push/Email is enabled for `type` (see NotificationChannelConfigEntity)
        /// and nudges any live-connected client via INotificationNotifier. Callers (MatchService.Swipe,
        /// SubscriptionService's webhook handlers) are expected to call this inline rather than fire-and-
        /// forget; Notify itself never throws, so it can't fail the caller's own request.
        /// </summary>
        Task Notify(Guid userid, NotificationType type, string title, string body, Guid? related_entity_id = null);

        Task<ProConnectResponse<NotificationPageResponse>> GetNotifications(Guid userid, NotificationPageRequest request);
        Task<ProConnectResponse<UnreadCountResponse>> GetUnreadCount(Guid userid);
        Task<ProConnectResponse<string>> MarkAsRead(Guid userid, Guid notificationid);
        Task<ProConnectResponse<string>> MarkAllAsRead(Guid userid);

        /// <summary>Registers (or re-registers) a Web/iOS/Android push token for the caller - see
        /// RegisterDeviceTokenRequest remarks.</summary>
        Task<ProConnectResponse<string>> RegisterDevice(Guid userid, RegisterDeviceTokenRequest request);
        Task<ProConnectResponse<string>> UnregisterDevice(Guid userid, string device_token);
    }
}
