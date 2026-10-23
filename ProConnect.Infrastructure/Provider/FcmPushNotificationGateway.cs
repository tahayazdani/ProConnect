using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProConnect.Domain.Config;
using ProConnect.Infrastructure.Interface;

namespace ProConnect.Infrastructure.Provider
{
    /// <summary>
    /// IPushNotificationGateway implementation backed by Firebase Cloud Messaging - see
    /// IPushNotificationGateway for the seam this sits behind. Every FirebaseAdmin SDK type (FirebaseApp,
    /// FirebaseMessaging, MulticastMessage...) is confined to this class, same "one adapter owns the whole
    /// vendor SDK" rule StripePaymentGateway follows for Stripe.net.
    ///
    /// FCM's single HTTP v1 send call fans out to Web Push, Android, and iOS (APNs) alike, so there's no
    /// DevicePlatform branching here at all - the token itself already encodes which platform it targets.
    /// </summary>
    public class FcmPushNotificationGateway : IPushNotificationGateway
    {
        private readonly PushConfig _config;
        private readonly ILogger<FcmPushNotificationGateway> _logger;

        // FirebaseApp.Create() may only be called once per process for the default app - guarded by a
        // static lock rather than per-instance state, matching StripeConfiguration.ApiKey's "configure once,
        // globally" shape (this class is registered Singleton anyway, so in practice there's only ever one
        // instance, but the static guard also protects against test/host-restart scenarios creating a
        // second instance before the first is disposed).
        private static FirebaseApp _app;
        private static readonly object _initLock = new object();

        public FcmPushNotificationGateway(IOptions<ProConnectConfig> proConnectConfig, ILogger<FcmPushNotificationGateway> logger)
        {
            _config = proConnectConfig.Value?.pushConfig
                ?? throw new InvalidOperationException("ProConnectConfig:PushConfig configuration section is missing.");
            _logger = logger;
        }

        // Lazy, not constructor-time, so a missing/blank credential only surfaces when a push is actually
        // attempted (same "built but dark" story as StripePaymentGateway setting an empty ApiKey - the SDK
        // itself throws a clear error at call time, which NotificationService's best-effort wrapper catches
        // and logs, rather than the whole app failing to start without real Firebase credentials yet).
        private FirebaseMessaging GetMessaging()
        {
            if (_app is null)
            {
                lock (_initLock)
                {
                    if (_app is null)
                    {
                        if (string.IsNullOrWhiteSpace(_config.serviceaccountjson))
                            throw new InvalidOperationException("ProConnectConfig:PushConfig:serviceaccountjson is not configured.");

                        _app = FirebaseApp.Create(new AppOptions
                        {
                            Credential = GoogleCredential.FromJson(_config.serviceaccountjson)
                        });
                    }
                }
            }

            return FirebaseMessaging.GetMessaging(_app);
        }

        public async Task SendPushAsync(IEnumerable<string> device_tokens, string title, string body, IDictionary<string, string> data)
        {
            var tokens = device_tokens?.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList() ?? new List<string>();
            if (tokens.Count == 0) return;

            var messaging = GetMessaging();

            var message = new MulticastMessage
            {
                Tokens = tokens,
                Notification = new Notification { Title = title, Body = body },
                Data = data is null ? null : new Dictionary<string, string>(data)
            };

            await messaging.SendEachForMulticastAsync(message);
        }
    }
}
