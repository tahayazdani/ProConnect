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
    }
}
