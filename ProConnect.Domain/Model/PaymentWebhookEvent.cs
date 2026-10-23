using System;
using ProConnect.Domain.Const;

namespace ProConnect.Domain.Model
{
    /// <summary>
    /// IPaymentGateway.ParseWebhookEvent's output - the Stripe Event (whose raw payload/signature verification
    /// is entirely inside StripePaymentGateway) boiled down to the handful of fields SubscriptionService.
    /// HandleWebhook actually branches on. Keeps every other layer free of Stripe.net types/event-type strings.
    /// </summary>
    public class PaymentWebhookEvent
    {
        public PaymentWebhookEvent()
        {

        }
        /// <summary>Stripe's own event id (evt_...) - used as an idempotency key so a redelivered webhook
        /// (Stripe retries on anything but a 2xx) doesn't double-apply, e.g. double-crediting a consumable.</summary>
        public string stripe_event_id { get; set; }

        /// <summary>Normalized event kind this codebase actually acts on - checkout completed, subscription
        /// updated/canceled, invoice payment succeeded/failed. Anything else SubscriptionService.HandleWebhook
        /// acknowledges (200) and ignores.</summary>
        public string event_type { get; set; }

        public Guid? userid { get; set; }
        public string stripe_customer_id { get; set; }
        public string stripe_subscription_id { get; set; }

        /// <summary>Set only for a subscription checkout's checkout.session.completed - which
        /// Const.PlanCode/BillingInterval was checked out, read back from the Checkout Session metadata
        /// StripePaymentGateway.CreateSubscriptionCheckoutSession set, so HandleWebhook doesn't have to
        /// infer a plan from a Stripe price id.</summary>
        public string plan_code { get; set; }
        public BillingInterval? billing_interval { get; set; }

        /// <summary>Set only for a one-off consumable purchase's checkout.session.completed - which
        /// ConsumableKey was bought and how many units, so HandleWebhook can credit the right wallet.</summary>
        public string consumable_key { get; set; }
        public int? consumable_quantity { get; set; }

        public PaymentStatus payment_status { get; set; }
        public decimal? amount { get; set; }
        public string currency { get; set; }
    }
}
