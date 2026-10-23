using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProConnect.Application.Interface;
using ProConnect.Domain.Request;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ProConnect.WebApi.Controllers
{
    /// <summary>
    /// Client-facing subscription/payment surface - see ISubscriptionService remarks. Every action except
    /// Webhook requires the normal JWT auth (global fallback policy in Program.cs); Webhook is the one
    /// endpoint in this entire API that is [AllowAnonymous], since Stripe itself is the caller and has no
    /// user JWT - its own signature (Stripe-Signature header, verified inside IPaymentGateway.
    /// ParseWebhookEvent) is the only auth it carries.
    /// </summary>
    public class SubscriptionController : BaseController
    {
        private readonly ILogger<SubscriptionController> _logger;
        private readonly ISubscriptionService _subscriptionService;

        public SubscriptionController(ILogger<SubscriptionController> logger, ISubscriptionService subscriptionService)
        {
            _logger = logger;
            _subscriptionService = subscriptionService;
        }

        [HttpGet]
        [Route("plans")]
        public async Task<IActionResult> GetPlans()
        {
            var response = await _subscriptionService.GetPlans();
            return StatusCode(response.statuscode, response);
        }

        [HttpGet]
        [Route("me")]
        public async Task<IActionResult> GetMySubscription()
        {
            var response = await _subscriptionService.GetMySubscription(CurrentUserId);
            return StatusCode(response.statuscode, response);
        }

        [HttpPost]
        [Route("checkout")]
        public async Task<IActionResult> Checkout(CheckoutRequest request)
        {
            var response = await _subscriptionService.Checkout(CurrentUserId, request);
            return StatusCode(response.statuscode, response);
        }

        [HttpPost]
        [Route("consumable/purchase")]
        public async Task<IActionResult> PurchaseConsumable(ConsumablePurchaseRequest request)
        {
            var response = await _subscriptionService.PurchaseConsumable(CurrentUserId, request);
            return StatusCode(response.statuscode, response);
        }

        [HttpPost]
        [Route("cancel")]
        public async Task<IActionResult> CancelSubscription([FromQuery] bool at_period_end = true)
        {
            var response = await _subscriptionService.CancelSubscription(CurrentUserId, at_period_end);
            return StatusCode(response.statuscode, response);
        }

        [HttpGet]
        [Route("transactions")]
        public async Task<IActionResult> GetTransactions([FromQuery] TransactionPageRequest request)
        {
            var response = await _subscriptionService.GetTransactions(CurrentUserId, request);
            return StatusCode(response.statuscode, response);
        }

        /// <summary>
        /// Stripe webhook receiver. Reads the raw request body itself (rather than relying on model
        /// binding) because Stripe's signature (Stripe-Signature header, verified in
        /// IPaymentGateway.ParseWebhookEvent via EventUtility.ConstructEvent) is computed over the exact
        /// raw bytes Stripe sent - any JSON re-serialization in between would break verification.
        ///
        /// A bad/missing/unconfigured signature throws StripeException out of ParseWebhookEvent, which -
        /// confirmed by live testing, not just assumed - is caught by the same generic, type-agnostic
        /// ExceptionHandlingMiddleware every other endpoint in this API shares, so it currently comes back
        /// as a 500 (ErrorCode.SYSTEM), not a 400; there is no 4xx path anywhere in this codebase today.
        /// That's still a non-2xx, so Stripe still retries - the original "400 so Stripe retries" framing
        /// in an earlier version of this comment was aspirational and never actually true. Deliberately
        /// left alone rather than threading a Stripe-specific exception type (or a new generic exception-
        /// to-statuscode convention) through ExceptionHandlingMiddleware, since IPaymentGateway's whole
        /// point is confining every Stripe SDK type to StripePaymentGateway - revisit only if 500-vs-400
        /// actually starts to matter for monitoring/alerting.
        /// </summary>
        [HttpPost]
        [Route("webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> Webhook()
        {
            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync();
            var signature = Request.Headers["Stripe-Signature"].ToString();

            var response = await _subscriptionService.HandleWebhook(payload, signature);
            return StatusCode(response.statuscode, response);
        }
    }
}
