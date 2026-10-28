using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ProConnect.Domain.Config;
using ProConnect.Domain.Const;
using ProConnect.Domain.Model;
using ProConnect.Infrastructure.Provider;
using Stripe;
using System;

namespace ProConnect.Tests.Infrastructure.Provider
{
    /// <summary>
    /// StripePaymentGateway confines the Stripe.net SDK entirely to itself - CreateSubscriptionCheckoutSession/
    /// CreateConsumableCheckoutSession/CancelSubscription make real outbound Stripe API calls with no injected
    /// abstraction over the SDK client, so only their pre-network guard clauses (missing Stripe price
    /// configuration) are unit-testable here; the actual SessionService/SubscriptionService calls are not.
    /// ParseWebhookEvent, however, is pure local logic - EventUtility.ConstructEvent only verifies an HMAC
    /// signature against the configured webhook secret (no network) - so it's fully testable by building
    /// real Stripe-shaped event JSON and signing it with EventUtility.GenerateSignatureHeader, the same helper
    /// Stripe.net itself uses to build a valid Stripe-Signature header.
    /// </summary>
    public class StripePaymentGatewayTests
    {
        private const string WebhookSecret = "whsec_test_secret";

        private readonly StripeConfig _config = new StripeConfig
        {
            secretkey = "sk_test_fake",
            publishablekey = "pk_test_fake",
            webhooksecret = WebhookSecret
        };

        private StripePaymentGateway CreateGateway()
        {
            var proConnectConfig = Options.Create(new ProConnectConfig { stripeConfig = _config });
            return new StripePaymentGateway(proConnectConfig, new Mock<ILogger<StripePaymentGateway>>().Object);
        }

        private static string Sign(string payload) => EventUtility.GenerateSignatureHeader(payload, WebhookSecret, null);

        // EventUtility.ParseEvent (called from inside ConstructEvent) throws a NullReferenceException of
        // its own if the event JSON omits "api_version" entirely - unrelated to anything
        // StripePaymentGateway/ParseWebhookEvent does, just how the installed Stripe.net build's
        // IsCompatibleApiVersion happens to behave on a null event api_version. Every well-formed webhook
        // payload Stripe actually sends includes it, so every payload built here does too.
        private static readonly string ApiVersion = StripeConfiguration.ApiVersion;

        [Fact]
        public async Task CreateSubscriptionCheckoutSession_WhenPlanHasNoStripePriceForInterval_ThrowsWithoutCallingStripe()
        {
            var gateway = CreateGateway();
            var plan = new SubscriptionPlan { plan_code = "PREMIUM", stripe_price_id_monthly = null, stripe_price_id_annual = null };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                gateway.CreateSubscriptionCheckoutSession(Guid.NewGuid(), plan, BillingInterval.Monthly, "https://success", "https://cancel", null));
        }

        [Fact]
        public async Task CreateConsumableCheckoutSession_WhenProductHasNoStripePrice_ThrowsWithoutCallingStripe()
        {
            var gateway = CreateGateway();
            var product = new ProConnect.Domain.Model.Product { consumable_key = "ROSE", stripe_price_id = null };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                gateway.CreateConsumableCheckoutSession(Guid.NewGuid(), product, 1, "https://success", "https://cancel", null));
        }

        [Fact]
        public void ParseWebhookEvent_WithBadSignature_ThrowsStripeException()
        {
            var gateway = CreateGateway();
            const string payload = "{\"id\":\"evt_1\",\"object\":\"event\",\"type\":\"checkout.session.completed\",\"data\":{\"object\":{}}}";

            Assert.Throws<StripeException>(() => gateway.ParseWebhookEvent(payload, "t=123,v1=not-a-real-signature"));
        }

        [Fact]
        public void ParseWebhookEvent_SubscriptionCheckoutCompleted_MapsUserPlanAndBillingInterval()
        {
            var userId = Guid.NewGuid();
            var payload = $@"{{
                ""id"": ""evt_sub_checkout"",
                ""object"": ""event"",
                ""type"": ""checkout.session.completed"",
                ""api_version"": ""{ApiVersion}"",
                ""data"": {{
                    ""object"": {{
                        ""id"": ""cs_123"",
                        ""object"": ""checkout.session"",
                        ""client_reference_id"": ""{userId}"",
                        ""customer"": ""cus_123"",
                        ""subscription"": ""sub_123"",
                        ""payment_status"": ""paid"",
                        ""amount_total"": 1999,
                        ""currency"": ""usd"",
                        ""mode"": ""subscription"",
                        ""metadata"": {{
                            ""userid"": ""{userId}"",
                            ""plan_code"": ""PREMIUM"",
                            ""billing_interval"": ""Annual""
                        }}
                    }}
                }}
            }}";

            var gateway = CreateGateway();
            var result = gateway.ParseWebhookEvent(payload, Sign(payload));

            Assert.Equal("evt_sub_checkout", result.stripe_event_id);
            Assert.Equal("checkout.session.completed", result.event_type);
            Assert.Equal(userId, result.userid);
            Assert.Equal("cus_123", result.stripe_customer_id);
            Assert.Equal("sub_123", result.stripe_subscription_id);
            Assert.Equal(PaymentStatus.Succeeded, result.payment_status);
            Assert.Equal(19.99m, result.amount);
            Assert.Equal("usd", result.currency);
            Assert.Equal("PREMIUM", result.plan_code);
            Assert.Equal(BillingInterval.Annual, result.billing_interval);
            Assert.Null(result.consumable_key);
        }

        [Fact]
        public void ParseWebhookEvent_ConsumableCheckoutCompleted_MapsConsumableKeyAndQuantity()
        {
            var userId = Guid.NewGuid();
            var payload = $@"{{
                ""id"": ""evt_consumable_checkout"",
                ""object"": ""event"",
                ""type"": ""checkout.session.completed"",
                ""api_version"": ""{ApiVersion}"",
                ""data"": {{
                    ""object"": {{
                        ""id"": ""cs_456"",
                        ""object"": ""checkout.session"",
                        ""client_reference_id"": ""{userId}"",
                        ""customer"": ""cus_456"",
                        ""payment_status"": ""unpaid"",
                        ""amount_total"": 500,
                        ""currency"": ""usd"",
                        ""mode"": ""payment"",
                        ""metadata"": {{
                            ""userid"": ""{userId}"",
                            ""consumable_key"": ""ROSE"",
                            ""quantity"": ""3""
                        }}
                    }}
                }}
            }}";

            var gateway = CreateGateway();
            var result = gateway.ParseWebhookEvent(payload, Sign(payload));

            Assert.Equal(userId, result.userid);
            Assert.Equal(PaymentStatus.Pending, result.payment_status);
            Assert.Equal("ROSE", result.consumable_key);
            Assert.Equal(3, result.consumable_quantity);
            Assert.Null(result.plan_code);
        }

        [Theory]
        [InlineData("customer.subscription.updated", "active", PaymentStatus.Succeeded)]
        [InlineData("customer.subscription.deleted", "canceled", PaymentStatus.Failed)]
        public void ParseWebhookEvent_SubscriptionStatusEvents_MapsUserIdFromMetadataAndStatus(
            string eventType, string stripeStatus, PaymentStatus expectedStatus)
        {
            var userId = Guid.NewGuid();
            var payload = $@"{{
                ""id"": ""evt_sub_status"",
                ""object"": ""event"",
                ""type"": ""{eventType}"",
                ""api_version"": ""{ApiVersion}"",
                ""data"": {{
                    ""object"": {{
                        ""id"": ""sub_789"",
                        ""object"": ""subscription"",
                        ""customer"": ""cus_789"",
                        ""status"": ""{stripeStatus}"",
                        ""metadata"": {{ ""userid"": ""{userId}"" }}
                    }}
                }}
            }}";

            var gateway = CreateGateway();
            var result = gateway.ParseWebhookEvent(payload, Sign(payload));

            Assert.Equal(eventType, result.event_type);
            Assert.Equal(userId, result.userid);
            Assert.Equal("cus_789", result.stripe_customer_id);
            Assert.Equal("sub_789", result.stripe_subscription_id);
            Assert.Equal(expectedStatus, result.payment_status);
        }

        [Theory]
        [InlineData("invoice.payment_succeeded", PaymentStatus.Succeeded)]
        [InlineData("invoice.payment_failed", PaymentStatus.Failed)]
        public void ParseWebhookEvent_InvoiceEvents_MapsCustomerSubscriptionAndAmount(string eventType, PaymentStatus expectedStatus)
        {
            var payload = $@"{{
                ""id"": ""evt_invoice"",
                ""object"": ""event"",
                ""type"": ""{eventType}"",
                ""api_version"": ""{ApiVersion}"",
                ""data"": {{
                    ""object"": {{
                        ""id"": ""in_123"",
                        ""object"": ""invoice"",
                        ""customer"": ""cus_321"",
                        ""amount_paid"": 2500,
                        ""currency"": ""cad"",
                        ""parent"": {{
                            ""type"": ""subscription_details"",
                            ""subscription_details"": {{ ""subscription"": ""sub_321"" }}
                        }}
                    }}
                }}
            }}";

            var gateway = CreateGateway();
            var result = gateway.ParseWebhookEvent(payload, Sign(payload));

            Assert.Equal("cus_321", result.stripe_customer_id);
            Assert.Equal("sub_321", result.stripe_subscription_id);
            Assert.Equal(expectedStatus, result.payment_status);
            Assert.Equal(25.00m, result.amount);
            Assert.Equal("cad", result.currency);
        }

        [Fact]
        public void ParseWebhookEvent_UnhandledEventType_ReturnsBareEventWithoutThrowing()
        {
            var payload = $@"{{
                ""id"": ""evt_unhandled"",
                ""object"": ""event"",
                ""type"": ""customer.created"",
                ""api_version"": ""{ApiVersion}"",
                ""data"": {{ ""object"": {{ ""id"": ""cus_999"", ""object"": ""customer"" }} }}
            }}";

            var gateway = CreateGateway();
            var result = gateway.ParseWebhookEvent(payload, Sign(payload));

            Assert.Equal("evt_unhandled", result.stripe_event_id);
            Assert.Equal("customer.created", result.event_type);
            Assert.Null(result.userid);
            Assert.Null(result.stripe_customer_id);
        }
    }
}
