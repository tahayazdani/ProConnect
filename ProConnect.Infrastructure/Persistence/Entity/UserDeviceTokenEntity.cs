using System;
using ProConnect.Domain.Const;

namespace ProConnect.Infrastructure.Persistence.Entity
{
    /// <summary>
    /// EF Core persistence model mirroring ProConnect.Domain.Model.UserDeviceToken - one row per registered
    /// Web/iOS/Android push token (NotificationController's POST /notifications/devices). A user can hold
    /// several (one per device/browser); device_token has its own unique index (see configuration) rather
    /// than keying on (userid, device_token), since the same physical token re-registering under a
    /// different userid (shared device, sign-out/sign-in as someone else) should move it, not duplicate it -
    /// otherwise a stale row would keep sending pushes for the account that's no longer signed in there.
    /// </summary>
    public class UserDeviceTokenEntity
    {
        public Guid userdevicetokenid { get; set; }
        public Guid userid { get; set; }
        public string device_token { get; set; }
        public DevicePlatform platform { get; set; }
        public DateTime created_at_utc { get; set; }
        public DateTime last_seen_at_utc { get; set; }
    }
}
