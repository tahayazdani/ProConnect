using System;
using ProConnect.Domain.Const;

namespace ProConnect.Domain.Model
{
    /// <summary>
    /// Domain-layer mirror of UserDeviceTokenEntity - a single Web/iOS/Android push registration for a user.
    /// A user can have several (one per device/browser they're logged into); IPushNotificationGateway sends
    /// to every token a user has registered.
    /// </summary>
    public class UserDeviceToken
    {
        public Guid userdevicetokenid { get; set; }
        public Guid userid { get; set; }
        public string device_token { get; set; }
        public DevicePlatform platform { get; set; }
        public DateTime created_at_utc { get; set; }

        // Bumped on every re-registration of the same token (e.g. app reopened) - not yet used to prune
        // stale tokens, but gives a future cleanup job something to filter on without a schema change.
        public DateTime last_seen_at_utc { get; set; }

        // BUG-23 fix (G-9): transient, not persisted - NotificationRepository.UpsertDeviceToken populates
        // this with whoever owned the token *before* this call, so NotificationService.RegisterDevice can
        // log an audit trail when a device_token silently changes owners (previous_userid != the caller's
        // userid), without altering the deliberate upsert-by-token-alone re-pointing behavior itself (see
        // this class's remarks above on why re-pointing happens at all - shared/reset device). Null means
        // either a brand-new token or the same user re-registering their own token.
        public Guid? previous_userid { get; set; }
    }
}
