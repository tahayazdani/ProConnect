using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using ProConnect.Domain.Config;
using ProConnect.Domain.Const;
using ProConnect.Domain.Model;
using ProConnect.Infrastructure.Interface;

namespace ProConnect.Infrastructure.Provider
{
    /// <summary>
    /// IPaymentGateway implementation backed by Stripe.net - see IPaymentGateway for the seam this sits
    /// behind. Every Stripe SDK type (Session, Subscription, Invoice, Event...) is confined to this class;
    /// everything it hands back or takes in is a plain Domain.Model type, same split IGeocodingProvider/
    /// NominatimGeocodingProvider use for Nominatim.
    ///
    /// userid is threaded through as both Session.ClientReferenceId (Stripe's own recommended mechanism
    /// for mapping a Checkout session back to "your" user) and a redundant Metadata["userid"] entry, so
    /// ParseWebhookEvent can recover it even from event types that don't carry ClientReferenceId directly
    /// (e.g. customer.subscription.updated only carries the Stripe customer/subscription id).
    /// </summary>
    public class StripePaymentGateway : IPaymentGateway
    {
        private readonly StripeConfig _config;
        private readonly ILogger<StripePaymentGateway> _logger;

        public StripePaymentGateway(IOptions<ProConnectConfig> proConnectConfig, ILogger<StripePaymentGateway> logger)
        {
            _config = proConnectConfig.Value?.stripeConfig
                ?? throw new InvalidOperationException("ProConnectConfig:StripeConfig configuration section is missing.");
            _logger = logger;

            // StripeConfiguration.ApiKey is a static/global setting in Stripe.net - set it once here rather
            // than per-call, same "configure once at the seam" approach as passing an API key into any
            // other SDK client.
            StripeConfiguration.ApiKey = _config.secretkey;
        }

        public async Task<CheckoutSessionResult> CreateSubscriptionCheckoutSession(
            Guid userid,
            SubscriptionPlan plan,
            BillingInterval interval,
            string success_url,
            string cancel_url,
            string existing_stripe_customer_id)
        {
            var priceId = interval == BillingInterval.Annual ? plan.stripe_price_id_annual : plan.stripe_price_id_monthly;
            if (string.IsNullOrEmpty(priceId))
                throw new InvalidOperationException($"Plan '{plan.plan_code}' has no Stripe price configured for interval '{interval}'.");

            var options = new SessionCreateOptions
            {
                Mode = "subscription",
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions { Price = priceId, Quantity = 1 }
                },
                SuccessUrl = success_url,
                CancelUrl = cancel_url,
                ClientReferenceId = userid.ToString(),
                Customer = string.IsNullOrEmpty(existing_stripe_customer_id) ? null : existing_stripe_customer_id,
                Metadata = new Dictionary<string, string>
                {
                    ["userid"] = userid.ToString(),
                    ["plan_code"] = plan.plan_code,
                    ["billing_interval"] = interval.ToString()
                }
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            return new CheckoutSessionResult { session_id = session.Id, checkout_url = session.Url };
        }

        public async Task<CheckoutSessionResult> CreateConsumableCheckoutSession(
            Guid userid,
            ProConnect.Domain.Model.Product product,
            int quantity,
            string success_url,
            string cancel_url,
            string existing_stripe_customer_id)
        {
            if (string.IsNullOrEmpty(product.stripe_price_id))
                throw new InvalidOperationException($"Product '{product.consumable_key}' has no Stripe price configured.");

            var options = new SessionCreateOptions
            {
                Mode = "payment",
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions { Price = product.stripe_price_id, Quantity = quantity }
                },
                SuccessUrl = success_url,
                CancelUrl = cancel_url,
                ClientReferenceId = userid.ToString(),
                Customer = string.IsNullOrEmpty(existing_stripe_customer_id) ? null : existing_stripe_customer_id,
                CustomerCreation = string.IsNullOrEmpty(existing_stripe_customer_id) ? "always" : null,
                Metadata = new Dictionary<string, string>
                {
                    ["userid"] = userid.ToString(),
                    ["consumable_key"] = product.consumable_key,
                    ["quantity"] = quantity.ToString()
                }
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            return new CheckoutSessionResult { session_id = session.Id, checkout_url = session.Url };
        }

        public async Task CancelSubscription(string stripe_subscription_id, bool at_period_end)
        {
            var service = new Stripe.SubscriptionService();

            if (at_period_end)
            {
                await service.UpdateAsync(stripe_subscription_id, new SubscriptionUpdateOptions { CancelAtPeriodEnd = true });
            }
            else
            {
                await service.CancelAsync(stripe_subscription_id, new SubscriptionCancelOptions());
            }
        }

        public PaymentWebhookEvent ParseWebhookEvent(string payload, string stripe_signature_header)
        {
            // Throws StripeException on a bad/missing signature - the controller lets that bubble up as a
            // 400, so we never act on an unverified payload (see IPaymentGateway remarks).
            var stripeEvent = EventUtility.ConstructEvent(payload, stripe_signature_header, _config.webhooksecret);

            var result = new PaymentWebhookEvent
            {
                stripe_event_id = stripeEvent.Id,
                event_type = stripeEvent.Type
            };

            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    {
                        var session = stripeEvent.Data.Object as Session;
                        if (session is null) break;

                        result.userid = TryParseUserId(session.ClientReferenceId, session.Metadata);
                        result.stripe_customer_id = session.CustomerId;
                        result.stripe_subscription_id = session.SubscriptionId;
                        result.payment_status = session.PaymentStatus == "paid" ? PaymentStatus.Succeeded : PaymentStatus.Pending;
                        result.amount = session.AmountTotal.HasValue ? session.AmountTotal.Value / 100m : null;
                        result.currency = session.Currency;

                        if (session.Mode == "payment" && session.Metadata != null
                            && session.Metadata.TryGetValue("consumable_key", out var consumableKey))
                        {
                            result.consumable_key = consumableKey;
                            result.consumable_quantity = session.Metadata.TryGetValue("quantity", out var qty) && int.TryParse(qty, out var parsedQty)
                                ? parsedQty
                                : 1;
                        }
                        else if (session.Mode == "subscription" && session.Metadata != null)
                        {
                            if (session.Metadata.TryGetValue("plan_code", out var planCode))
                                result.plan_code = planCode;

                            if (session.Metadata.TryGetValue("billing_interval", out var intervalRaw)
                                && Enum.TryParse<BillingInterval>(intervalRaw, out var parsedInterval))
                                result.billing_interval = parsedInterval;
                        }
                        break;
                    }

                case "customer.subscription.updated":
                case "customer.subscription.deleted":
                    {
                        var subscription = stripeEvent.Data.Object as Stripe.Subscription;
                        if (subscription is null) break;

                        result.userid = TryParseUserId(null, subscription.Metadata);
                        result.stripe_customer_id = subscription.CustomerId;
                        result.stripe_subscription_id = subscription.Id;
                        result.payment_status = subscription.Status == "active" ? PaymentStatus.Succeeded : PaymentStatus.Failed;
                        break;
                    }

                case "invoice.payment_succeeded":
                case "invoice.payment_failed":
                    {
                        var invoice = stripeEvent.Data.Object as Invoice;
                        if (invoice is null) break;

                        result.stripe_customer_id = invoice.CustomerId;
                        // Stripe.net 53's Invoice no longer exposes SubscriptionId directly - the
                        // subscription-invoice link moved under Parent.SubscriptionDetails as of the
                        // 2025-ish Invoices API restructuring.
                        result.stripe_subscription_id = invoice.Parent?.SubscriptionDetails?.SubscriptionId;
                        result.payment_status = stripeEvent.Type == "invoice.payment_succeeded" ? PaymentStatus.Succeeded : PaymentStatus.Failed;
                        result.amount = invoice.AmountPaid / 100m;
                        result.currency = invoice.Currency;
                        break;
                    }

                default:
                    _logger.LogInformation("StripePaymentGateway.ParseWebhookEvent ignoring unhandled event type '{EventType}'.", stripeEvent.Type);
                    break;
            }

            return result;
        }

        private static Guid? TryParseUserId(string clientReferenceId, IDictionary<string, string> metadata)
        {
            if (!string.IsNullOrEmpty(clientReferenceId) && Guid.TryParse(clientReferenceId, out var fromRef))
                return fromRef;

            if (metadata != null && metadata.TryGetValue("userid", out var raw) && Guid.TryParse(raw, out var fromMeta))
                return fromMeta;

            return null;
        }
    }
}
