using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stripe;
using ProConnect.Application.Interface;
using ProConnect.Domain.Const;
using ProConnect.Domain.Model;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using ProConnect.Infrastructure.Interface;

namespace ProConnect.Application.Service
{
    /// <summary>
    /// See ISubscriptionService remarks. Nothing here talks to Stripe directly (IPaymentGateway) or
    /// decides what a plan grants (IFeatureGateService) - this is purely orchestration: validate the
    /// request against the subscription_plan/product catalog, delegate to the right seam, and (for
    /// HandleWebhook) persist the result and invalidate the affected user's cached EffectivePlan.
    ///
    /// Known simplification: checkout.session.completed sets a provisional current_period_end_utc
    /// (now + 1 month/year based on billing_interval) rather than expanding the Stripe Subscription object
    /// for its authoritative period - acceptable because Stripe's customer.subscription.updated webhook
    /// (which does carry it) follows checkout.session.completed within the same flow in practice. Tighten
    /// this (e.g. have ParseWebhookEvent surface period dates on every subscription-shaped event) if exact
    /// period_end precision ever matters before the next webhook lands.
    /// </summary>
    public class SubscriptionService : ISubscriptionService
    {
        private readonly ISubscriptionRepository _subscriptionRepository;
        private readonly IPaymentGateway _paymentGateway;
        private readonly IFeatureGateService _featureGateService;
        private readonly INotificationService _notificationService;
        private readonly ILogger<SubscriptionService> _logger;

        public SubscriptionService(
            ISubscriptionRepository subscriptionRepository,
            IPaymentGateway paymentGateway,
            IFeatureGateService featureGateService,
            INotificationService notificationService,
            ILogger<SubscriptionService> logger)
        {
            _subscriptionRepository = subscriptionRepository;
            _paymentGateway = paymentGateway;
            _featureGateService = featureGateService;
            _notificationService = notificationService;
            _logger = logger;
        }

        public async Task<ProConnectResponse<List<SubscriptionPlanResponse>>> GetPlans()
        {
            try
            {
                var plans = await _subscriptionRepository.GetActivePlans();

                var body = plans.Select(p => new SubscriptionPlanResponse
                {
                    plan_code = p.plan_code,
                    display_name = p.display_name,
                    description = p.description,
                    price_monthly = p.price_monthly,
                    price_annual = p.price_annual,
                    sort_order = p.sort_order,
                    features = p.features.Select(f => new PlanFeatureResponse
                    {
                        feature_key = f.feature_key,
                        is_enabled = f.is_enabled,
                        limit_value = f.limit_value,
                        reset_period = f.reset_period
                    }).ToList()
                }).ToList();

                return new ProConnectResponse<List<SubscriptionPlanResponse>> { body = body, message = "Plans have been fetched successfully." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetPlans failed.");
                throw;
            }
        }

        public async Task<ProConnectResponse<UserSubscriptionResponse>> GetMySubscription(Guid userid)
        {
            try
            {
                var subscription = await _subscriptionRepository.GetUserSubscription(userid);

                // No row at all resolves to Free, same fallback IFeatureGateService.ResolveEffectivePlan uses.
                if (subscription is null)
                {
                    var freePlan = await _subscriptionRepository.GetPlanByCode(PlanCode.Free);
                    return new ProConnectResponse<UserSubscriptionResponse>
                    {
                        body = new UserSubscriptionResponse
                        {
                            plan_code = PlanCode.Free,
                            display_name = freePlan?.display_name ?? "Free",
                            status = SubscriptionStatus.Active,
                            billing_interval = null,
                            current_period_end_utc = null,
                            cancel_at_period_end = false
                        },
                        message = "Subscription has been fetched successfully."
                    };
                }

                var plan = await _subscriptionRepository.GetPlanById(subscription.planid);

                return new ProConnectResponse<UserSubscriptionResponse>
                {
                    body = new UserSubscriptionResponse
                    {
                        plan_code = subscription.plan_code ?? plan?.plan_code,
                        display_name = plan?.display_name ?? subscription.plan_code,
                        status = subscription.status,
                        billing_interval = subscription.billing_interval,
                        current_period_end_utc = subscription.current_period_end_utc,
                        cancel_at_period_end = subscription.cancel_at_period_end
                    },
                    message = "Subscription has been fetched successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GetMySubscription failed for userid '{userid}'.");
                throw;
            }
        }

        public async Task<ProConnectResponse<CheckoutResponse>> Checkout(Guid userid, CheckoutRequest request)
        {
            try
            {
                if (string.Equals(request?.plan_code, PlanCode.Free, StringComparison.OrdinalIgnoreCase))
                    return new ProConnectResponse<CheckoutResponse>
                    {
                        issuccessfull = false,
                        errormessage = "The Free plan does not require checkout.",
                        message = "The Free plan does not require checkout."
                    };

                var plan = await _subscriptionRepository.GetPlanByCode(request?.plan_code);
                if (plan is null || !plan.is_active)
                    return new ProConnectResponse<CheckoutResponse>
                    {
                        issuccessfull = false,
                        errormessage = "The requested plan could not be found.",
                        message = "The requested plan could not be found."
                    };

                var existing = await _subscriptionRepository.GetUserSubscription(userid);

                var result = await _paymentGateway.CreateSubscriptionCheckoutSession(
                    userid, plan, request.billing_interval, request.success_url, request.cancel_url, existing?.stripe_customer_id);

                return new ProConnectResponse<CheckoutResponse>
                {
                    body = new CheckoutResponse { checkout_url = result.checkout_url },
                    message = "Checkout session has been created successfully."
                };
            }
            // BUG-05 fix: Stripe.net's StripeException.Message can carry Stripe-internal detail (API key
            // fragments, internal object ids, account/rate-limit specifics) - letting it fall through to
            // the generic `catch (Exception ex) { throw; }` below meant ExceptionHandlingMiddleware put
            // that raw text straight into the client-facing `errormessage`. Caught specifically here (and
            // in PurchaseConsumable/CancelSubscription) so the client gets a generic, safe message while
            // the real exception is still logged server-side for debugging.
            catch (StripeException ex)
            {
                _logger.LogError(ex, $"Checkout failed for userid '{userid}' due to a payment provider error.");
                return new ProConnectResponse<CheckoutResponse>
                {
                    issuccessfull = false,
                    errormessage = "Payment provider error, please try again later.",
                    message = "Payment provider error, please try again later."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Checkout failed for userid '{userid}'.");
                throw;
            }
        }

        public async Task<ProConnectResponse<CheckoutResponse>> PurchaseConsumable(Guid userid, ConsumablePurchaseRequest request)
        {
            try
            {
                if (request is null || request.quantity < 1)
                    return new ProConnectResponse<CheckoutResponse>
                    {
                        issuccessfull = false,
                        errormessage = "A valid quantity is required.",
                        message = "A valid quantity is required."
                    };

                var product = await _subscriptionRepository.GetActiveProductByConsumableKey(request.consumable_key);
                if (product is null)
                    return new ProConnectResponse<CheckoutResponse>
                    {
                        issuccessfull = false,
                        errormessage = "The requested product could not be found.",
                        message = "The requested product could not be found."
                    };

                var existing = await _subscriptionRepository.GetUserSubscription(userid);

                var result = await _paymentGateway.CreateConsumableCheckoutSession(
                    userid, product, request.quantity, request.success_url, request.cancel_url, existing?.stripe_customer_id);

                return new ProConnectResponse<CheckoutResponse>
                {
                    body = new CheckoutResponse { checkout_url = result.checkout_url },
                    message = "Checkout session has been created successfully."
                };
            }
            // BUG-05 fix: see the matching catch in Checkout above.
            catch (StripeException ex)
            {
                _logger.LogError(ex, $"PurchaseConsumable failed for userid '{userid}' due to a payment provider error.");
                return new ProConnectResponse<CheckoutResponse>
                {
                    issuccessfull = false,
                    errormessage = "Payment provider error, please try again later.",
                    message = "Payment provider error, please try again later."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"PurchaseConsumable failed for userid '{userid}'.");
                throw;
            }
        }

        public async Task<ProConnectResponse<string>> CancelSubscription(Guid userid, bool at_period_end = true)
        {
            try
            {
                var subscription = await _subscriptionRepository.GetUserSubscription(userid);
                if (subscription is null || string.IsNullOrEmpty(subscription.stripe_subscription_id))
                    return new ProConnectResponse<string>
                    {
                        issuccessfull = false,
                        errormessage = "No active subscription was found.",
                        message = "No active subscription was found."
                    };

                // BUG-07 fix: without this, a double-cancel (double-click, or a retry after a
                // slow/timed-out first request) unconditionally fell through to the live
                // _paymentGateway.CancelSubscription call below against a subscription Stripe already
                // considers canceled, which throws and (pre-BUG-05-fix) surfaced as a raw 500. Short-
                // circuiting here makes a repeat cancel idempotent instead of erroring, and skips an
                // unnecessary live call to Stripe for a subscription that's already gone there too.
                if (subscription.status == SubscriptionStatus.Canceled)
                    return new ProConnectResponse<string>
                    {
                        body = "ok",
                        message = "Subscription is already canceled."
                    };

                await _paymentGateway.CancelSubscription(subscription.stripe_subscription_id, at_period_end);

                if (at_period_end)
                {
                    subscription.cancel_at_period_end = true;
                }
                else
                {
                    subscription.status = SubscriptionStatus.Canceled;
                    subscription.cancel_at_period_end = false;
                }

                await _subscriptionRepository.UpsertUserSubscription(subscription);
                await _featureGateService.InvalidateEffectivePlan(userid);

                return new ProConnectResponse<string>
                {
                    body = "ok",
                    message = at_period_end
                        ? "Your subscription will be canceled at the end of the current billing period."
                        : "Your subscription has been canceled."
                };
            }
            // BUG-05 fix: see the matching catch in Checkout above.
            catch (StripeException ex)
            {
                _logger.LogError(ex, $"CancelSubscription failed for userid '{userid}' due to a payment provider error.");
                return new ProConnectResponse<string>
                {
                    issuccessfull = false,
                    errormessage = "Payment provider error, please try again later.",
                    message = "Payment provider error, please try again later."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"CancelSubscription failed for userid '{userid}'.");
                throw;
            }
        }

        public async Task<ProConnectResponse<string>> HandleWebhook(string payload, string stripe_signature_header)
        {
            PaymentWebhookEvent webhookEvent;

            // BUG-06 fix: ParseWebhookEvent throws Stripe.StripeException on a bad/missing/unconfigured
            // Stripe-Signature header. This used to fall through to the generic `catch (Exception ex) {
            // throw; }` further down, which ExceptionHandlingMiddleware maps to a 500 - despite the
            // controller's own (now-corrected) doc comment having at one point claimed this came back as
            // a 400. Caught here, before the normal processing try/catch, specifically so a bad signature
            // gets a real 400: Stripe's retry/alerting behavior treats 4xx as "don't retry, bad request"
            // and 5xx as "retry, treat as the receiver's outage" - a signature mismatch is the former, not
            // the latter, and shouldn't trip server-error alerting or cause Stripe to keep retrying an
            // event that will never verify.
            try
            {
                webhookEvent = _paymentGateway.ParseWebhookEvent(payload, stripe_signature_header);
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex, "SubscriptionService.HandleWebhook rejected a webhook with a bad/missing Stripe-Signature.");
                return new ProConnectResponse<string>
                {
                    statuscode = 400,
                    issuccessfull = false,
                    errormessage = "Invalid webhook signature.",
                    message = "Invalid webhook signature."
                };
            }
            // BUG-16 fix: Stripe's own event schema documents `api_version` as nullable, but Stripe.net's
            // EventUtility.ConstructEvent (called with its 3-arg overload, so throwOnApiVersionMismatch
            // defaults to true) unconditionally calls IsCompatibleApiVersion(eventApiVersion) to decide
            // whether to throw - and that comparison throws a bare NullReferenceException (not a
            // StripeException) when eventApiVersion is null, regardless of throwOnApiVersionMismatch.
            // That NRE used to fall straight through the StripeException catch above into
            // ExceptionHandlingMiddleware's generic 500, for a payload whose signature was perfectly
            // valid. A 500 tells Stripe "our outage, please retry" - but this failure is deterministic
            // and will reproduce identically on every retry, so it should be a 400 ("don't retry, bad
            // request") like the signature-failure path right above it.
            catch (Exception ex)
            {
                _logger.LogError(ex, "SubscriptionService.HandleWebhook failed to parse a validly-signed webhook payload (e.g. Stripe.net choking on a null/legacy api_version).");
                return new ProConnectResponse<string>
                {
                    statuscode = 400,
                    issuccessfull = false,
                    errormessage = "Unable to process webhook payload.",
                    message = "Unable to process webhook payload."
                };
            }

            try
            {
                // BUG-19 fix: everything from here down - the idempotency check, the event-type dispatch,
                // and every handler's side effects plus its CreateTransaction insert - runs while holding
                // an advisory lock keyed on this exact stripe_event_id (see
                // ISubscriptionRepository.AcquireEventLock remarks). A genuinely concurrent second
                // delivery of the same event_id blocks here until the first has fully committed, then
                // correctly finds TransactionExistsByStripeEventId true and exits via the idempotent path
                // below instead of racing into a double side effect (e.g. a consumable balance credited
                // twice for one purchase - reproduced in BUGS_FOUND.md BUG-19). Events with no
                // stripe_event_id at all (shouldn't happen from real Stripe, but defensively handled
                // elsewhere in this method) skip locking entirely, same as they already skip the
                // idempotency check below.
                IAsyncDisposable eventLock = string.IsNullOrEmpty(webhookEvent.stripe_event_id)
                    ? null
                    : await _subscriptionRepository.AcquireEventLock(webhookEvent.stripe_event_id);

                try
                {
                    // Idempotency - a Stripe retry (anything but a 2xx response) must never double-apply,
                    // e.g. double-crediting a consumable - see PaymentTransactionEntity remarks.
                    if (!string.IsNullOrEmpty(webhookEvent.stripe_event_id)
                        && await _subscriptionRepository.TransactionExistsByStripeEventId(webhookEvent.stripe_event_id))
                    {
                        return new ProConnectResponse<string> { body = "ok", message = "Event already processed." };
                    }

                    switch (webhookEvent.event_type)
                    {
                        case "checkout.session.completed":
                            if (!string.IsNullOrEmpty(webhookEvent.consumable_key))
                                await HandleConsumableCheckoutCompleted(webhookEvent);
                            else if (!string.IsNullOrEmpty(webhookEvent.plan_code))
                                await HandleSubscriptionCheckoutCompleted(webhookEvent);
                            break;

                        case "customer.subscription.updated":
                            await HandleSubscriptionStatusChanged(webhookEvent, SubscriptionStatus.PastDue, trustEmbeddedStatus: true);
                            break;

                        case "customer.subscription.deleted":
                            // BUG-25 fix (G-31): trustEmbeddedStatus=false - a .deleted event means Stripe has
                            // removed the subscription object, full stop; that fact must win over whatever
                            // status string happens to be embedded in this particular payload. Previously this
                            // called the same payment_status-driven mapping as .updated, so a .deleted event
                            // whose embedded Subscription.Status was (unusually, e.g. out-of-order delivery)
                            // still "active" mapped payment_status to Succeeded and left/set the local row
                            // Active instead of Canceled - reproduced live in BUGS_FOUND.md BUG-25 / G-31.
                            await HandleSubscriptionStatusChanged(webhookEvent, SubscriptionStatus.Canceled, trustEmbeddedStatus: false);
                            break;

                        case "invoice.payment_succeeded":
                        case "invoice.payment_failed":
                            await HandleInvoiceEvent(webhookEvent);
                            break;

                        default:
                            _logger.LogInformation("SubscriptionService.HandleWebhook ignoring unhandled event type '{EventType}'.", webhookEvent.event_type);
                            break;
                    }

                    return new ProConnectResponse<string> { body = "ok", message = "Webhook processed." };
                }
                finally
                {
                    if (eventLock != null)
                        await eventLock.DisposeAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HandleWebhook failed.");
                throw;
            }
        }

        private async Task HandleSubscriptionCheckoutCompleted(PaymentWebhookEvent evt)
        {
            if (!evt.userid.HasValue)
            {
                _logger.LogWarning("checkout.session.completed (subscription) had no resolvable userid - stripe_event_id '{EventId}'.", evt.stripe_event_id);
                return;
            }

            var plan = await _subscriptionRepository.GetPlanByCode(evt.plan_code);
            if (plan is null)
            {
                _logger.LogWarning("checkout.session.completed referenced unknown plan_code '{PlanCode}'.", evt.plan_code);
                return;
            }

            var interval = evt.billing_interval ?? BillingInterval.Monthly;
            var now = DateTime.UtcNow;
            var periodEnd = interval == BillingInterval.Annual ? now.AddYears(1) : now.AddMonths(1);

            var existing = await _subscriptionRepository.GetUserSubscription(evt.userid.Value);

            await _subscriptionRepository.UpsertUserSubscription(new UserSubscription
            {
                userid = evt.userid.Value,
                planid = plan.planid,
                plan_code = plan.plan_code,
                status = SubscriptionStatus.Active,
                billing_interval = interval,
                stripe_customer_id = evt.stripe_customer_id ?? existing?.stripe_customer_id,
                stripe_subscription_id = evt.stripe_subscription_id,
                current_period_start_utc = now,
                current_period_end_utc = periodEnd,
                cancel_at_period_end = false
            });

            await _featureGateService.InvalidateEffectivePlan(evt.userid.Value);

            await _subscriptionRepository.CreateTransaction(new PaymentTransaction
            {
                userid = evt.userid.Value,
                type = PaymentTransactionType.Subscription,
                description = $"Subscribed to {plan.display_name} ({interval})",
                amount = evt.amount ?? (interval == BillingInterval.Annual ? plan.price_annual : plan.price_monthly),
                currency = evt.currency ?? "usd",
                status = PaymentStatus.Succeeded,
                stripe_event_id = evt.stripe_event_id,
                created_at_utc = now
            });

            // INotificationService.Notify never throws (see its own remarks) - called inline, same as
            // every other caller, so a notification failure can never unwind the subscription write above.
            await _notificationService.Notify(evt.userid.Value, NotificationType.SubscriptionActivated,
                "Subscription activated!", $"You're now on the {plan.display_name} plan ({interval}).");
        }

        private async Task HandleConsumableCheckoutCompleted(PaymentWebhookEvent evt)
        {
            if (!evt.userid.HasValue)
            {
                _logger.LogWarning("checkout.session.completed (consumable) had no resolvable userid - stripe_event_id '{EventId}'.", evt.stripe_event_id);
                return;
            }

            var product = await _subscriptionRepository.GetActiveProductByConsumableKey(evt.consumable_key);
            if (product is null)
            {
                _logger.LogWarning("checkout.session.completed referenced unknown consumable_key '{ConsumableKey}'.", evt.consumable_key);
                return;
            }

            var bundles = evt.consumable_quantity ?? 1;
            var totalUnits = product.quantity_granted * bundles;

            await _subscriptionRepository.AdjustConsumableBalance(evt.userid.Value, evt.consumable_key, totalUnits);

            await _subscriptionRepository.CreateTransaction(new PaymentTransaction
            {
                userid = evt.userid.Value,
                type = PaymentTransactionType.Consumable,
                description = $"Purchased {totalUnits} x {evt.consumable_key}",
                amount = evt.amount ?? product.price * bundles,
                currency = evt.currency ?? product.currency,
                status = PaymentStatus.Succeeded,
                stripe_event_id = evt.stripe_event_id,
                created_at_utc = DateTime.UtcNow
            });
        }

        // customer.subscription.updated/deleted - resolved by stripe_subscription_id since those events
        // don't reliably carry our own userid metadata unless explicitly set on the Subscription object
        // itself. `fallbackStatus` is applied whenever the mapping doesn't resolve to Active - either
        // because the event's own payment_status wasn't Succeeded (see IPaymentGateway.ParseWebhookEvent),
        // or, for a .deleted event, unconditionally (see trustEmbeddedStatus below).
        //
        // BUG-25 fix (G-31): `trustEmbeddedStatus` - when false (passed by the .deleted dispatch case),
        // the embedded Subscription.Status snapshot is never consulted at all and `fallbackStatus` always
        // wins. A .deleted event is Stripe's authoritative statement that the subscription object itself
        // is gone; the status string embedded in that same payload can still read "active" in some
        // out-of-order/edge deliveries, and letting that override the deletion fact previously left (or
        // even re-set) the local row as Active after Stripe had already deleted the subscription.
        private async Task HandleSubscriptionStatusChanged(PaymentWebhookEvent evt, SubscriptionStatus fallbackStatus, bool trustEmbeddedStatus)
        {
            var subscription = !string.IsNullOrEmpty(evt.stripe_subscription_id)
                ? await _subscriptionRepository.GetUserSubscriptionByStripeSubscriptionId(evt.stripe_subscription_id)
                : null;

            if (subscription is null)
            {
                _logger.LogWarning("Subscription status webhook referenced unknown stripe_subscription_id '{SubscriptionId}'.", evt.stripe_subscription_id);
                return;
            }

            subscription.status = trustEmbeddedStatus && evt.payment_status == PaymentStatus.Succeeded
                ? SubscriptionStatus.Active
                : fallbackStatus;

            await _subscriptionRepository.UpsertUserSubscription(subscription);
            await _featureGateService.InvalidateEffectivePlan(subscription.userid);

            // SUB-09 fix: this handler used to be the only webhook handler that never called
            // CreateTransaction, so HandleWebhook's TransactionExistsByStripeEventId idempotency guard had
            // nothing to find on a replayed customer.subscription.updated/.deleted event and always fell
            // through to full reprocessing - observed live as two duplicate SubscriptionCanceled
            // notifications from a single replayed event id. This status-change event has no monetary
            // amount of its own (amount 0), but writing a row here is what makes it a member of the same
            // idempotency ledger as every other webhook-driven write - see BUGS_FOUND.md.
            await _subscriptionRepository.CreateTransaction(new PaymentTransaction
            {
                userid = subscription.userid,
                type = PaymentTransactionType.Subscription,
                description = $"Subscription status changed to {subscription.status}",
                amount = 0,
                currency = "usd",
                status = evt.payment_status,
                stripe_event_id = evt.stripe_event_id,
                created_at_utc = DateTime.UtcNow
            });

            // Only the customer.subscription.deleted path (fallbackStatus Canceled) realistically lands
            // here with a Canceled status - customer.subscription.updated's own fallback is PastDue, which
            // deliberately has no notification of its own (PaymentFailed, fired from HandleInvoiceEvent on
            // invoice.payment_failed, already covers the "something's wrong with billing" case for the
            // user-facing side of a PastDue transition).
            if (subscription.status == SubscriptionStatus.Canceled)
                await _notificationService.Notify(subscription.userid, NotificationType.SubscriptionCanceled,
                    "Subscription canceled", "Your subscription has been canceled.");
        }

        private async Task HandleInvoiceEvent(PaymentWebhookEvent evt)
        {
            var subscription = !string.IsNullOrEmpty(evt.stripe_subscription_id)
                ? await _subscriptionRepository.GetUserSubscriptionByStripeSubscriptionId(evt.stripe_subscription_id)
                : null;

            if (subscription is null)
            {
                _logger.LogWarning("Invoice webhook referenced unknown stripe_subscription_id '{SubscriptionId}'.", evt.stripe_subscription_id);
                return;
            }

            if (evt.payment_status == PaymentStatus.Failed)
            {
                subscription.status = SubscriptionStatus.PastDue;
                await _subscriptionRepository.UpsertUserSubscription(subscription);
                await _featureGateService.InvalidateEffectivePlan(subscription.userid);
            }

            await _subscriptionRepository.CreateTransaction(new PaymentTransaction
            {
                userid = subscription.userid,
                type = PaymentTransactionType.Subscription,
                description = evt.payment_status == PaymentStatus.Succeeded ? "Subscription renewal" : "Subscription renewal failed",
                amount = evt.amount ?? 0,
                currency = evt.currency ?? "usd",
                status = evt.payment_status,
                stripe_event_id = evt.stripe_event_id,
                created_at_utc = DateTime.UtcNow
            });

            // Known duplicate-notification edge case, same spirit as this file's "Known simplification"
            // doc-comment above: Stripe fires invoice.payment_succeeded alongside checkout.session.completed
            // for a brand-new subscription's first invoice, so a user can get both SubscriptionActivated
            // and SubscriptionRenewed in quick succession on initial signup. Not worth threading "is this
            // the very first invoice" state through PaymentWebhookEvent just to suppress one push.
            if (evt.payment_status == PaymentStatus.Succeeded)
                await _notificationService.Notify(subscription.userid, NotificationType.SubscriptionRenewed,
                    "Subscription renewed", "Your subscription has been renewed successfully.");
            else if (evt.payment_status == PaymentStatus.Failed)
                await _notificationService.Notify(subscription.userid, NotificationType.PaymentFailed,
                    "Payment failed", "We couldn't process your subscription payment. Please update your payment method to avoid losing access.");
        }

        public async Task<ProConnectResponse<TransactionPageResponse>> GetTransactions(Guid userid, TransactionPageRequest request)
        {
            try
            {
                request ??= new TransactionPageRequest();

                var offset = (request.page - 1) * request.pagesize;
                var transactions = await _subscriptionRepository.GetTransactions(userid, offset, request.pagesize);
                var totalCount = await _subscriptionRepository.CountTransactions(userid);

                var results = transactions.Select(t => new TransactionResponse
                {
                    transactionid = t.transactionid,
                    type = t.type,
                    description = t.description,
                    amount = t.amount,
                    currency = t.currency,
                    status = t.status,
                    created_at_utc = t.created_at_utc
                }).ToList();

                return new ProConnectResponse<TransactionPageResponse>
                {
                    body = new TransactionPageResponse
                    {
                        results = results,
                        total_count = totalCount,
                        page = request.page,
                        pagesize = request.pagesize,
                        has_more = (long)request.page * request.pagesize < totalCount
                    },
                    message = "Transactions have been fetched successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GetTransactions failed for userid '{userid}'.");
                throw;
            }
        }
    }
}
