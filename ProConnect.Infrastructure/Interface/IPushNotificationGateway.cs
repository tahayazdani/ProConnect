using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Interface
{
    /// <summary>
    /// Seam between INotificationService and whichever OS-push provider is actually wired up -
    /// FcmPushNotificationGateway today (Firebase Cloud Messaging), which covers Web Push, Android, and iOS
    /// (via FCM's APNs bridge) through one unified send call, so nothing above this interface needs to know
    /// or care which DevicePlatform a given token came from. Same seam pattern as IPaymentGateway/
    /// IGeocodingProvider - swapping providers, or just rotating credentials, is a PushConfig edit, not a
    /// rewrite.
    ///
    /// Best-effort only, same contract as IMessageNotifier - a push failure (bad/expired token, provider
    /// outage, no credentials configured) must never fail the request that already durably persisted the
    /// Notification row; callers (NotificationService) are expected to swallow/log exceptions from this
    /// rather than let them propagate.
    /// </summary>
    public interface IPushNotificationGateway
    {
        /// <summary>
        /// Sends the same title/body/data payload to every token in device_tokens - a user can have several
        /// registered devices (web + phone) and all of them should receive the push. `data` is a small
        /// string dictionary (notification type/related_entity_id) so a client can deep-link on tap. A null
        /// or empty device_tokens is a no-op, not an error - most users won't have registered a device yet.
        /// </summary>
        Task SendPushAsync(IEnumerable<string> device_tokens, string title, string body, IDictionary<string, string> data);
    }
}
