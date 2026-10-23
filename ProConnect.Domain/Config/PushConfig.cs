namespace ProConnect.Domain.Config
{
    /// <summary>
    /// Config for FcmPushNotificationGateway (Infrastructure/Provider) - Firebase Cloud Messaging, which
    /// covers Web Push, Android, and iOS (via FCM's APNs bridge) through one unified send call, so this one
    /// section is the entire push-provider story for all three client platforms. Same "built but dark until
    /// real credentials exist" story as StripeConfig - edit these values (via user-secrets locally, a secret
    /// manager elsewhere), no code change, to actually start sending pushes.
    /// </summary>
    public class PushConfig
    {
        public PushConfig()
        {

        }

        /// <summary>Full contents (not a file path) of a Firebase service-account JSON key - FCM's HTTP v1
        /// API credential. Set via `dotnet user-secrets set "ProConnectConfig:PushConfig:serviceaccountjson"
        /// "<value>"` in ProConnect.WebApi; do not commit a real value here.</summary>
        public string serviceaccountjson { get; set; }
    }
}
