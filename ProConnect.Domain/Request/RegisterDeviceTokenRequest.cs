using ProConnect.Domain.Const;

namespace ProConnect.Domain.Request
{
    /// <summary>
    /// Registers (or re-registers) a push token for the caller - see NotificationController's
    /// POST /notifications/devices. Re-registering the same device_token for the same user is a no-op
    /// upsert (just bumps last_seen_at_utc); the same token showing up under a *different* userid (e.g. a
    /// shared device, or a sign-out/sign-in as someone else) re-points it to the new user, since a stale
    /// token still pointing at the previous account would otherwise leak pushes to the wrong person.
    /// </summary>
    public class RegisterDeviceTokenRequest
    {
        public RegisterDeviceTokenRequest()
        {

        }
        public string device_token { get; set; }
        public DevicePlatform platform { get; set; }
    }
}
