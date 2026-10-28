using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ProConnect.Domain.Config;
using ProConnect.Infrastructure.Provider;
using System;
using System.Collections.Generic;

namespace ProConnect.Tests.Infrastructure.Provider
{
    /// <summary>
    /// FcmPushNotificationGateway confines the FirebaseAdmin SDK entirely to itself with no injected
    /// abstraction over it, so its actual send path (FirebaseMessaging.SendEachForMulticastAsync) can't be
    /// unit tested without real Firebase credentials. What's testable: the empty-after-filter short-circuit
    /// (returns before touching Firebase at all) and the lazy "built but dark" credential check in
    /// GetMessaging, which only throws once a push is actually attempted with a non-empty token list. No
    /// test here ever supplies a real/parseable service-account JSON, so the static FirebaseApp field this
    /// class guards with a lock is never actually initialized - safe to run in any order/count within the
    /// same test process.
    /// </summary>
    public class FcmPushNotificationGatewayTests
    {
        private FcmPushNotificationGateway CreateGateway(string serviceAccountJson)
        {
            var proConnectConfig = Options.Create(new ProConnectConfig { pushConfig = new PushConfig { serviceaccountjson = serviceAccountJson } });
            return new FcmPushNotificationGateway(proConnectConfig, new Mock<ILogger<FcmPushNotificationGateway>>().Object);
        }

        [Fact]
        public async Task SendPushAsync_WithNullTokens_ReturnsWithoutAttemptingFirebaseInit()
        {
            var gateway = CreateGateway(serviceAccountJson: null);

            // Would throw (missing credential) if it got as far as GetMessaging() - it doesn't, since the
            // token list is empty after filtering, so this completing without throwing is the assertion.
            await gateway.SendPushAsync(null, "title", "body", null);
        }

        [Fact]
        public async Task SendPushAsync_WithOnlyBlankTokens_ReturnsWithoutAttemptingFirebaseInit()
        {
            var gateway = CreateGateway(serviceAccountJson: null);

            await gateway.SendPushAsync(new List<string> { "", "   ", null }, "title", "body", null);
        }

        [Fact]
        public async Task SendPushAsync_WithTokensButNoServiceAccountConfigured_ThrowsInvalidOperationException()
        {
            var gateway = CreateGateway(serviceAccountJson: null);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                gateway.SendPushAsync(new List<string> { "device-token-1" }, "title", "body", null));
        }
    }
}
