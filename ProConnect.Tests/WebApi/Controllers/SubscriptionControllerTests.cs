using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using ProConnect.Application.Interface;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using ProConnect.WebApi.Controllers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text;

namespace ProConnect.Tests.WebApi.Controllers
{
    /// <summary>
    /// SubscriptionController is a thin passthrough to ISubscriptionService - same conventions as the
    /// other controller test classes, except Webhook, which reads the raw request body/Stripe-Signature
    /// header directly instead of model binding (see its remarks) and needs its HttpContext built up
    /// accordingly rather than just a claims principal.
    /// </summary>
    public class SubscriptionControllerTests
    {
        private readonly Mock<ISubscriptionService> _subscriptionService = new();
        private readonly Guid _userId = Guid.NewGuid();

        private SubscriptionController CreateController()
        {
            var controller = new SubscriptionController(new Mock<ILogger<SubscriptionController>>().Object, _subscriptionService.Object);

            var identity = new ClaimsIdentity(new[] { new Claim("userid", _userId.ToString()) });
            var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
        }

        [Fact]
        public async Task GetPlans_ForwardsServiceResponseWithoutCurrentUserId()
        {
            var expected = new ProConnectResponse<List<SubscriptionPlanResponse>> { statuscode = 200, body = new List<SubscriptionPlanResponse>() };
            _subscriptionService.Setup(s => s.GetPlans()).ReturnsAsync(expected);

            var result = await CreateController().GetPlans() as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _subscriptionService.Verify(s => s.GetPlans(), Times.Once);
        }

        [Fact]
        public async Task GetMySubscription_PassesCurrentUserId()
        {
            var expected = new ProConnectResponse<UserSubscriptionResponse> { statuscode = 200, body = new UserSubscriptionResponse() };
            _subscriptionService.Setup(s => s.GetMySubscription(_userId)).ReturnsAsync(expected);

            var result = await CreateController().GetMySubscription() as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _subscriptionService.Verify(s => s.GetMySubscription(_userId), Times.Once);
        }

        [Fact]
        public async Task Checkout_PassesCurrentUserIdAndRequest()
        {
            var request = new CheckoutRequest { plan_code = "PREMIUM" };
            var expected = new ProConnectResponse<CheckoutResponse> { statuscode = 200, body = new CheckoutResponse() };
            _subscriptionService.Setup(s => s.Checkout(_userId, request)).ReturnsAsync(expected);

            var result = await CreateController().Checkout(request) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _subscriptionService.Verify(s => s.Checkout(_userId, request), Times.Once);
        }

        [Fact]
        public async Task PurchaseConsumable_PassesCurrentUserIdAndRequest()
        {
            var request = new ConsumablePurchaseRequest { consumable_key = "ROSE" };
            var expected = new ProConnectResponse<CheckoutResponse> { statuscode = 200, body = new CheckoutResponse() };
            _subscriptionService.Setup(s => s.PurchaseConsumable(_userId, request)).ReturnsAsync(expected);

            var result = await CreateController().PurchaseConsumable(request) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _subscriptionService.Verify(s => s.PurchaseConsumable(_userId, request), Times.Once);
        }

        [Fact]
        public async Task CancelSubscription_DefaultsAtPeriodEndToTrue()
        {
            var expected = new ProConnectResponse<string> { statuscode = 200, body = "ok" };
            _subscriptionService.Setup(s => s.CancelSubscription(_userId, true)).ReturnsAsync(expected);

            var result = await CreateController().CancelSubscription() as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _subscriptionService.Verify(s => s.CancelSubscription(_userId, true), Times.Once);
        }

        [Fact]
        public async Task CancelSubscription_PassesExplicitAtPeriodEndValue()
        {
            var expected = new ProConnectResponse<string> { statuscode = 200, body = "ok" };
            _subscriptionService.Setup(s => s.CancelSubscription(_userId, false)).ReturnsAsync(expected);

            var result = await CreateController().CancelSubscription(false) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _subscriptionService.Verify(s => s.CancelSubscription(_userId, false), Times.Once);
        }

        [Fact]
        public async Task GetTransactions_PassesCurrentUserIdAndRequest()
        {
            var request = new TransactionPageRequest { page = 3 };
            var expected = new ProConnectResponse<TransactionPageResponse> { statuscode = 200, body = new TransactionPageResponse() };
            _subscriptionService.Setup(s => s.GetTransactions(_userId, request)).ReturnsAsync(expected);

            var result = await CreateController().GetTransactions(request) as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _subscriptionService.Verify(s => s.GetTransactions(_userId, request), Times.Once);
        }

        [Fact]
        public async Task Webhook_ReadsRawBodyAndSignatureHeaderAndForwardsToService()
        {
            const string payload = "{\"id\":\"evt_1\"}";
            const string signature = "t=123,v1=abc";

            var controller = new SubscriptionController(new Mock<ILogger<SubscriptionController>>().Object, _subscriptionService.Object);
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            httpContext.Request.Headers["Stripe-Signature"] = signature;
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            var expected = new ProConnectResponse<string> { statuscode = 200, body = "ok" };
            _subscriptionService.Setup(s => s.HandleWebhook(payload, signature)).ReturnsAsync(expected);

            var result = await controller.Webhook() as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            _subscriptionService.Verify(s => s.HandleWebhook(payload, signature), Times.Once);
        }

        [Fact]
        public async Task Webhook_WhenSignatureInvalid_ForwardsCleanFailure()
        {
            const string payload = "{\"id\":\"evt_1\"}";
            const string signature = "bad-signature";

            var controller = new SubscriptionController(new Mock<ILogger<SubscriptionController>>().Object, _subscriptionService.Object);
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            httpContext.Request.Headers["Stripe-Signature"] = signature;
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            var expected = new ProConnectResponse<string> { statuscode = 400, issuccessfull = false, errormessage = "Invalid signature" };
            _subscriptionService.Setup(s => s.HandleWebhook(payload, signature)).ReturnsAsync(expected);

            var result = await controller.Webhook() as ObjectResult;

            Assert.NotNull(result);
            Assert.Equal(400, result.StatusCode);
        }
    }
}
