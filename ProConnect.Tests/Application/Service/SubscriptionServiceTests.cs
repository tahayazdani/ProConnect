using Microsoft.Extensions.Logging;
using Moq;
using ProConnect.Application.Interface;
using ProConnect.Application.Service;
using ProConnect.Domain.Const;
using ProConnect.Domain.Model;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using ProConnect.Infrastructure.Interface;
using Stripe;

namespace ProConnect.Tests.Application.Service
{
    /// <summary>
    /// Covers SubscriptionService's orchestration of the subscription/product catalog against mocked
    /// ISubscriptionRepository/IPaymentGateway/IFeatureGateService/INotificationService - no Cache
    /// dependency exists on this service (unlike FeatureGateService/SearchService/MatchService), so
    /// CreateService wires mocks directly. Focus is the main success/failure branch of every public
    /// method, plus HandleWebhook's event-type dispatch (its own switch statement is the bulk of this
    /// service's logic) and the StripeException-to-safe-message mapping repeated across
    /// Checkout/PurchaseConsumable/CancelSubscription (see the service's own BUG-05 doc comments).
    /// AcquireEventLock's advisory-lock handle is mocked as a plain Mock&lt;IAsyncDisposable&gt;.Object -
    /// the locking mechanics themselves live in the repository, not this service.
    /// </summary>
    public class SubscriptionServiceTests
    {
        private readonly Mock<ISubscriptionRepository> _subscriptionRepository = new();
        private readonly Mock<IPaymentGateway> _paymentGateway = new();
        private readonly Mock<IFeatureGateService> _featureGateService = new();
        private readonly Mock<INotificationService> _notificationService = new();

        public SubscriptionServiceTests()
        {
            _subscriptionRepository.Setup(r => r.AcquireEventLock(It.IsAny<string>()))
                .ReturnsAsync(new Mock<IAsyncDisposable>().Object);
            _subscriptionRepository.Setup(r => r.TransactionExistsByStripeEventId(It.IsAny<string>()))
                .ReturnsAsync(false);
        }

        private ProConnect.Application.Service.SubscriptionService CreateService()
        {
            return new ProConnect.Application.Service.SubscriptionService(
                _subscriptionRepository.Object,
                _paymentGateway.Object,
                _featureGateService.Object,
                _notificationService.Object,
                new Mock<ILogger<ProConnect.Application.Service.SubscriptionService>>().Object);
        }

        // --- GetPlans ---

        [Fact]
        public async Task GetPlans_ReturnsFlattenedFeatureMatrixFromRepository()
        {
            _subscriptionRepository.Setup(r => r.GetActivePlans()).ReturnsAsync(new List<SubscriptionPlan>
            {
                new SubscriptionPlan
                {
                    plan_code = PlanCode.Plus,
                    display_name = "Plus",
                    price_monthly = 9.99m,
                    price_annual = 99.99m,
                    sort_order = 1,
                    features = new List<PlanFeature>
                    {
                        new PlanFeature { feature_key = FeatureKey.DailyLikes, is_enabled = true, limit_value = 100 }
                    }
                }
            });

            var service = CreateService();

            var response = await service.GetPlans();

            Assert.True(response.issuccessfull);
            Assert.Single(response.body!);
            Assert.Equal(PlanCode.Plus, response.body![0].plan_code);
            Assert.Single(response.body![0].features);
            Assert.Equal(FeatureKey.DailyLikes, response.body![0].features[0].feature_key);
        }

        // --- GetMySubscription ---

        [Fact]
        public async Task GetMySubscription_WithNoSubscriptionRow_FallsBackToFreePlan()
        {
            var userid = Guid.NewGuid();
            _subscriptionRepository.Setup(r => r.GetUserSubscription(userid)).ReturnsAsync((UserSubscription)null!);
            _subscriptionRepository.Setup(r => r.GetPlanByCode(PlanCode.Free)).ReturnsAsync(new SubscriptionPlan { plan_code = PlanCode.Free, display_name = "Free" });

            var service = CreateService();

            var response = await service.GetMySubscription(userid);

            Assert.True(response.issuccessfull);
            Assert.Equal(PlanCode.Free, response.body!.plan_code);
            Assert.Equal(SubscriptionStatus.Active, response.body!.status);
            Assert.False(response.body!.cancel_at_period_end);
        }

        [Fact]
        public async Task GetMySubscription_WithExistingSubscription_ReturnsItsDetails()
        {
            var userid = Guid.NewGuid();
            var planid = Guid.NewGuid();
            var periodEnd = DateTime.UtcNow.AddDays(10);
            _subscriptionRepository.Setup(r => r.GetUserSubscription(userid)).ReturnsAsync(new UserSubscription
            {
                userid = userid,
                planid = planid,
                plan_code = PlanCode.Premium,
                status = SubscriptionStatus.Active,
                billing_interval = BillingInterval.Annual,
                current_period_end_utc = periodEnd,
                cancel_at_period_end = true
            });
            _subscriptionRepository.Setup(r => r.GetPlanById(planid)).ReturnsAsync(new SubscriptionPlan { planid = planid, plan_code = PlanCode.Premium, display_name = "Premium" });

            var service = CreateService();

            var response = await service.GetMySubscription(userid);

            Assert.True(response.issuccessfull);
            Assert.Equal(PlanCode.Premium, response.body!.plan_code);
            Assert.Equal("Premium", response.body!.display_name);
            Assert.Equal(BillingInterval.Annual, response.body!.billing_interval);
            Assert.Equal(periodEnd, response.body!.current_period_end_utc);
            Assert.True(response.body!.cancel_at_period_end);
        }

        // --- Checkout ---

        [Fact]
        public async Task Checkout_WithFreePlanCode_ReturnsFailureWithoutTouchingPaymentGateway()
        {
            var service = CreateService();

            var response = await service.Checkout(Guid.NewGuid(), new CheckoutRequest { plan_code = PlanCode.Free });

            Assert.False(response.issuccessfull);
            Assert.Equal("The Free plan does not require checkout.", response.errormessage);
            _paymentGateway.Verify(g => g.CreateSubscriptionCheckoutSession(
                It.IsAny<Guid>(), It.IsAny<SubscriptionPlan>(), It.IsAny<BillingInterval>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task Checkout_WithUnknownPlanCode_ReturnsFailure()
        {
            _subscriptionRepository.Setup(r => r.GetPlanByCode("bogus")).ReturnsAsync((SubscriptionPlan)null!);

            var service = CreateService();

            var response = await service.Checkout(Guid.NewGuid(), new CheckoutRequest { plan_code = "bogus" });

            Assert.False(response.issuccessfull);
            Assert.Equal("The requested plan could not be found.", response.errormessage);
        }

        [Fact]
        public async Task Checkout_Success_DelegatesToPaymentGatewayWithPlanAndExistingCustomerId()
        {
            var userid = Guid.NewGuid();
            var plan = new SubscriptionPlan { planid = Guid.NewGuid(), plan_code = PlanCode.Plus, display_name = "Plus", is_active = true };
            var request = new CheckoutRequest
            {
                plan_code = PlanCode.Plus,
                billing_interval = BillingInterval.Annual,
                success_url = "https://app/success",
                cancel_url = "https://app/cancel"
            };
            _subscriptionRepository.Setup(r => r.GetPlanByCode(PlanCode.Plus)).ReturnsAsync(plan);
            _subscriptionRepository.Setup(r => r.GetUserSubscription(userid)).ReturnsAsync(new UserSubscription { userid = userid, stripe_customer_id = "cus_123" });
            _paymentGateway.Setup(g => g.CreateSubscriptionCheckoutSession(userid, plan, BillingInterval.Annual, request.success_url, request.cancel_url, "cus_123"))
                .ReturnsAsync(new CheckoutSessionResult { checkout_url = "https://stripe/session" });

            var service = CreateService();

            var response = await service.Checkout(userid, request);

            Assert.True(response.issuccessfull);
            Assert.Equal("https://stripe/session", response.body!.checkout_url);
            _paymentGateway.Verify(g => g.CreateSubscriptionCheckoutSession(userid, plan, BillingInterval.Annual, request.success_url, request.cancel_url, "cus_123"), Times.Once);
        }

        [Fact]
        public async Task Checkout_WhenPaymentGatewayThrowsStripeException_ReturnsSafeGenericError()
        {
            var userid = Guid.NewGuid();
            var plan = new SubscriptionPlan { planid = Guid.NewGuid(), plan_code = PlanCode.Plus, is_active = true };
            _subscriptionRepository.Setup(r => r.GetPlanByCode(PlanCode.Plus)).ReturnsAsync(plan);
            _subscriptionRepository.Setup(r => r.GetUserSubscription(userid)).ReturnsAsync((UserSubscription)null!);
            _paymentGateway.Setup(g => g.CreateSubscriptionCheckoutSession(
                    It.IsAny<Guid>(), It.IsAny<SubscriptionPlan>(), It.IsAny<BillingInterval>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new StripeException("card declined: sk_live_abcdef"));

            var service = CreateService();

            var response = await service.Checkout(userid, new CheckoutRequest { plan_code = PlanCode.Plus });

            Assert.False(response.issuccessfull);
            Assert.Equal("Payment provider error, please try again later.", response.errormessage);
            Assert.DoesNotContain("sk_live", response.errormessage);
        }

        // --- PurchaseConsumable ---

        [Fact]
        public async Task PurchaseConsumable_WithInvalidQuantity_ReturnsFailureWithoutTouchingRepository()
        {
            var service = CreateService();

            var response = await service.PurchaseConsumable(Guid.NewGuid(), new ConsumablePurchaseRequest { consumable_key = ConsumableKey.Rose, quantity = 0 });

            Assert.False(response.issuccessfull);
            Assert.Equal("A valid quantity is required.", response.errormessage);
            _subscriptionRepository.Verify(r => r.GetActiveProductByConsumableKey(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task PurchaseConsumable_WithUnknownConsumableKey_ReturnsFailure()
        {
            _subscriptionRepository.Setup(r => r.GetActiveProductByConsumableKey("bogus")).ReturnsAsync((ProConnect.Domain.Model.Product)null!);

            var service = CreateService();

            var response = await service.PurchaseConsumable(Guid.NewGuid(), new ConsumablePurchaseRequest { consumable_key = "bogus", quantity = 1 });

            Assert.False(response.issuccessfull);
            Assert.Equal("The requested product could not be found.", response.errormessage);
        }

        [Fact]
        public async Task PurchaseConsumable_Success_DelegatesToPaymentGateway()
        {
            var userid = Guid.NewGuid();
            var product = new ProConnect.Domain.Model.Product { productid = Guid.NewGuid(), consumable_key = ConsumableKey.Boost, quantity_granted = 5 };
            var request = new ConsumablePurchaseRequest { consumable_key = ConsumableKey.Boost, quantity = 2, success_url = "s", cancel_url = "c" };
            _subscriptionRepository.Setup(r => r.GetActiveProductByConsumableKey(ConsumableKey.Boost)).ReturnsAsync(product);
            _subscriptionRepository.Setup(r => r.GetUserSubscription(userid)).ReturnsAsync((UserSubscription)null!);
            _paymentGateway.Setup(g => g.CreateConsumableCheckoutSession(userid, product, 2, "s", "c", (string)null!))
                .ReturnsAsync(new CheckoutSessionResult { checkout_url = "https://stripe/consumable" });

            var service = CreateService();

            var response = await service.PurchaseConsumable(userid, request);

            Assert.True(response.issuccessfull);
            Assert.Equal("https://stripe/consumable", response.body!.checkout_url);
            _paymentGateway.Verify(g => g.CreateConsumableCheckoutSession(userid, product, 2, "s", "c", (string)null!), Times.Once);
        }

        [Fact]
        public async Task PurchaseConsumable_WhenPaymentGatewayThrowsStripeException_ReturnsSafeGenericError()
        {
            var userid = Guid.NewGuid();
            var product = new ProConnect.Domain.Model.Product { productid = Guid.NewGuid(), consumable_key = ConsumableKey.Rose };
            _subscriptionRepository.Setup(r => r.GetActiveProductByConsumableKey(ConsumableKey.Rose)).ReturnsAsync(product);
            _subscriptionRepository.Setup(r => r.GetUserSubscription(userid)).ReturnsAsync((UserSubscription)null!);
            _paymentGateway.Setup(g => g.CreateConsumableCheckoutSession(
                    It.IsAny<Guid>(), It.IsAny<ProConnect.Domain.Model.Product>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new StripeException("internal stripe detail"));

            var service = CreateService();

            var response = await service.PurchaseConsumable(userid, new ConsumablePurchaseRequest { consumable_key = ConsumableKey.Rose, quantity = 1 });

            Assert.False(response.issuccessfull);
            Assert.Equal("Payment provider error, please try again later.", response.errormessage);
        }

        // --- CancelSubscription ---

        [Fact]
        public async Task CancelSubscription_WithNoActiveSubscription_ReturnsFailure()
        {
            var userid = Guid.NewGuid();
            _subscriptionRepository.Setup(r => r.GetUserSubscription(userid)).ReturnsAsync((UserSubscription)null!);

            var service = CreateService();

            var response = await service.CancelSubscription(userid);

            Assert.False(response.issuccessfull);
            Assert.Equal("No active subscription was found.", response.errormessage);
            _paymentGateway.Verify(g => g.CancelSubscription(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task CancelSubscription_WhenAlreadyCanceled_ReturnsIdempotentSuccessWithoutCallingGateway()
        {
            var userid = Guid.NewGuid();
            _subscriptionRepository.Setup(r => r.GetUserSubscription(userid)).ReturnsAsync(new UserSubscription
            {
                userid = userid,
                stripe_subscription_id = "sub_123",
                status = SubscriptionStatus.Canceled
            });

            var service = CreateService();

            var response = await service.CancelSubscription(userid);

            Assert.True(response.issuccessfull);
            Assert.Equal("ok", response.body);
            _paymentGateway.Verify(g => g.CancelSubscription(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task CancelSubscription_AtPeriodEnd_SetsFlagAndInvalidatesCache()
        {
            var userid = Guid.NewGuid();
            var subscription = new UserSubscription
            {
                userid = userid,
                stripe_subscription_id = "sub_123",
                status = SubscriptionStatus.Active,
                cancel_at_period_end = false
            };
            _subscriptionRepository.Setup(r => r.GetUserSubscription(userid)).ReturnsAsync(subscription);

            var service = CreateService();

            var response = await service.CancelSubscription(userid, at_period_end: true);

            Assert.True(response.issuccessfull);
            _paymentGateway.Verify(g => g.CancelSubscription("sub_123", true), Times.Once);
            _subscriptionRepository.Verify(r => r.UpsertUserSubscription(It.Is<UserSubscription>(s => s.cancel_at_period_end && s.status == SubscriptionStatus.Active)), Times.Once);
            _featureGateService.Verify(f => f.InvalidateEffectivePlan(userid), Times.Once);
        }

        [Fact]
        public async Task CancelSubscription_Immediate_SetsStatusCanceled()
        {
            var userid = Guid.NewGuid();
            var subscription = new UserSubscription
            {
                userid = userid,
                stripe_subscription_id = "sub_123",
                status = SubscriptionStatus.Active
            };
            _subscriptionRepository.Setup(r => r.GetUserSubscription(userid)).ReturnsAsync(subscription);

            var service = CreateService();

            var response = await service.CancelSubscription(userid, at_period_end: false);

            Assert.True(response.issuccessfull);
            _paymentGateway.Verify(g => g.CancelSubscription("sub_123", false), Times.Once);
            _subscriptionRepository.Verify(r => r.UpsertUserSubscription(It.Is<UserSubscription>(s => !s.cancel_at_period_end && s.status == SubscriptionStatus.Canceled)), Times.Once);
        }

        [Fact]
        public async Task CancelSubscription_WhenPaymentGatewayThrowsStripeException_ReturnsSafeGenericError()
        {
            var userid = Guid.NewGuid();
            _subscriptionRepository.Setup(r => r.GetUserSubscription(userid)).ReturnsAsync(new UserSubscription
            {
                userid = userid,
                stripe_subscription_id = "sub_123",
                status = SubscriptionStatus.Active
            });
            _paymentGateway.Setup(g => g.CancelSubscription("sub_123", true)).ThrowsAsync(new StripeException("boom"));

            var service = CreateService();

            var response = await service.CancelSubscription(userid);

            Assert.False(response.issuccessfull);
            Assert.Equal("Payment provider error, please try again later.", response.errormessage);
        }

        // --- HandleWebhook ---

        [Fact]
        public async Task HandleWebhook_WithBadSignature_Returns400()
        {
            _paymentGateway.Setup(g => g.ParseWebhookEvent("payload", "bad-sig")).Throws(new StripeException("signature mismatch"));

            var service = CreateService();

            var response = await service.HandleWebhook("payload", "bad-sig");

            Assert.False(response.issuccessfull);
            Assert.Equal(400, response.statuscode);
            Assert.Equal("Invalid webhook signature.", response.errormessage);
        }

        [Fact]
        public async Task HandleWebhook_WhenParseThrowsUnexpectedException_Returns400()
        {
            _paymentGateway.Setup(g => g.ParseWebhookEvent("payload", "sig")).Throws(new NullReferenceException("api_version was null"));

            var service = CreateService();

            var response = await service.HandleWebhook("payload", "sig");

            Assert.False(response.issuccessfull);
            Assert.Equal(400, response.statuscode);
            Assert.Equal("Unable to process webhook payload.", response.errormessage);
        }

        [Fact]
        public async Task HandleWebhook_WhenEventAlreadyProcessed_ReturnsOkWithoutDispatching()
        {
            var evt = new PaymentWebhookEvent { stripe_event_id = "evt_1", event_type = "checkout.session.completed", plan_code = PlanCode.Plus, userid = Guid.NewGuid() };
            _paymentGateway.Setup(g => g.ParseWebhookEvent("payload", "sig")).Returns(evt);
            _subscriptionRepository.Setup(r => r.TransactionExistsByStripeEventId("evt_1")).ReturnsAsync(true);

            var service = CreateService();

            var response = await service.HandleWebhook("payload", "sig");

            Assert.True(response.issuccessfull);
            Assert.Equal("ok", response.body);
            _subscriptionRepository.Verify(r => r.GetPlanByCode(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task HandleWebhook_SubscriptionCheckoutCompleted_UpsertsSubscriptionAndNotifies()
        {
            var userid = Guid.NewGuid();
            var plan = new SubscriptionPlan { planid = Guid.NewGuid(), plan_code = PlanCode.Plus, display_name = "Plus", price_monthly = 9.99m };
            var evt = new PaymentWebhookEvent
            {
                stripe_event_id = "evt_sub",
                event_type = "checkout.session.completed",
                plan_code = PlanCode.Plus,
                billing_interval = BillingInterval.Monthly,
                userid = userid,
                stripe_customer_id = "cus_1",
                stripe_subscription_id = "sub_1"
            };
            _paymentGateway.Setup(g => g.ParseWebhookEvent("payload", "sig")).Returns(evt);
            _subscriptionRepository.Setup(r => r.GetPlanByCode(PlanCode.Plus)).ReturnsAsync(plan);
            _subscriptionRepository.Setup(r => r.GetUserSubscription(userid)).ReturnsAsync((UserSubscription)null!);

            var service = CreateService();

            var response = await service.HandleWebhook("payload", "sig");

            Assert.True(response.issuccessfull);
            _subscriptionRepository.Verify(r => r.UpsertUserSubscription(It.Is<UserSubscription>(s =>
                s.userid == userid && s.plan_code == PlanCode.Plus && s.status == SubscriptionStatus.Active && s.stripe_subscription_id == "sub_1")), Times.Once);
            _featureGateService.Verify(f => f.InvalidateEffectivePlan(userid), Times.Once);
            _subscriptionRepository.Verify(r => r.CreateTransaction(It.Is<PaymentTransaction>(t => t.userid == userid && t.stripe_event_id == "evt_sub")), Times.Once);
            _notificationService.Verify(n => n.Notify(userid, NotificationType.SubscriptionActivated, It.IsAny<string>(), It.IsAny<string>(), null), Times.Once);
        }

        [Fact]
        public async Task HandleWebhook_ConsumableCheckoutCompleted_CreditsBalanceAndCreatesTransaction()
        {
            var userid = Guid.NewGuid();
            var product = new ProConnect.Domain.Model.Product { productid = Guid.NewGuid(), consumable_key = ConsumableKey.Rose, quantity_granted = 5, price = 4.99m, currency = "usd" };
            var evt = new PaymentWebhookEvent
            {
                stripe_event_id = "evt_con",
                event_type = "checkout.session.completed",
                consumable_key = ConsumableKey.Rose,
                consumable_quantity = 2,
                userid = userid
            };
            _paymentGateway.Setup(g => g.ParseWebhookEvent("payload", "sig")).Returns(evt);
            _subscriptionRepository.Setup(r => r.GetActiveProductByConsumableKey(ConsumableKey.Rose)).ReturnsAsync(product);

            var service = CreateService();

            var response = await service.HandleWebhook("payload", "sig");

            Assert.True(response.issuccessfull);
            _subscriptionRepository.Verify(r => r.AdjustConsumableBalance(userid, ConsumableKey.Rose, 10), Times.Once);
            _subscriptionRepository.Verify(r => r.CreateTransaction(It.Is<PaymentTransaction>(t => t.userid == userid && t.type == PaymentTransactionType.Consumable)), Times.Once);
        }

        [Fact]
        public async Task HandleWebhook_SubscriptionUpdated_UpdatesStatusAndInvalidatesCache()
        {
            var userid = Guid.NewGuid();
            var subscription = new UserSubscription { userid = userid, stripe_subscription_id = "sub_1", status = SubscriptionStatus.Active };
            var evt = new PaymentWebhookEvent
            {
                stripe_event_id = "evt_upd",
                event_type = "customer.subscription.updated",
                stripe_subscription_id = "sub_1",
                payment_status = PaymentStatus.Succeeded
            };
            _paymentGateway.Setup(g => g.ParseWebhookEvent("payload", "sig")).Returns(evt);
            _subscriptionRepository.Setup(r => r.GetUserSubscriptionByStripeSubscriptionId("sub_1")).ReturnsAsync(subscription);

            var service = CreateService();

            var response = await service.HandleWebhook("payload", "sig");

            Assert.True(response.issuccessfull);
            _subscriptionRepository.Verify(r => r.UpsertUserSubscription(It.Is<UserSubscription>(s => s.status == SubscriptionStatus.Active)), Times.Once);
            _featureGateService.Verify(f => f.InvalidateEffectivePlan(userid), Times.Once);
            _notificationService.Verify(n => n.Notify(It.IsAny<Guid>(), NotificationType.SubscriptionCanceled, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
        }

        [Fact]
        public async Task HandleWebhook_SubscriptionDeleted_ForcesCanceledRegardlessOfEmbeddedStatus()
        {
            var userid = Guid.NewGuid();
            var subscription = new UserSubscription { userid = userid, stripe_subscription_id = "sub_1", status = SubscriptionStatus.Active };
            var evt = new PaymentWebhookEvent
            {
                stripe_event_id = "evt_del",
                event_type = "customer.subscription.deleted",
                stripe_subscription_id = "sub_1",
                // Embedded status snapshot says Succeeded/Active even though Stripe just deleted the subscription -
                // trustEmbeddedStatus=false means this must NOT win (BUG-25 fix).
                payment_status = PaymentStatus.Succeeded
            };
            _paymentGateway.Setup(g => g.ParseWebhookEvent("payload", "sig")).Returns(evt);
            _subscriptionRepository.Setup(r => r.GetUserSubscriptionByStripeSubscriptionId("sub_1")).ReturnsAsync(subscription);

            var service = CreateService();

            var response = await service.HandleWebhook("payload", "sig");

            Assert.True(response.issuccessfull);
            _subscriptionRepository.Verify(r => r.UpsertUserSubscription(It.Is<UserSubscription>(s => s.status == SubscriptionStatus.Canceled)), Times.Once);
            _notificationService.Verify(n => n.Notify(userid, NotificationType.SubscriptionCanceled, It.IsAny<string>(), It.IsAny<string>(), null), Times.Once);
        }

        [Fact]
        public async Task HandleWebhook_InvoicePaymentSucceeded_NotifiesRenewed()
        {
            var userid = Guid.NewGuid();
            var subscription = new UserSubscription { userid = userid, stripe_subscription_id = "sub_1", status = SubscriptionStatus.Active };
            var evt = new PaymentWebhookEvent
            {
                stripe_event_id = "evt_inv_ok",
                event_type = "invoice.payment_succeeded",
                stripe_subscription_id = "sub_1",
                payment_status = PaymentStatus.Succeeded
            };
            _paymentGateway.Setup(g => g.ParseWebhookEvent("payload", "sig")).Returns(evt);
            _subscriptionRepository.Setup(r => r.GetUserSubscriptionByStripeSubscriptionId("sub_1")).ReturnsAsync(subscription);

            var service = CreateService();

            var response = await service.HandleWebhook("payload", "sig");

            Assert.True(response.issuccessfull);
            Assert.Equal(SubscriptionStatus.Active, subscription.status);
            _subscriptionRepository.Verify(r => r.UpsertUserSubscription(It.IsAny<UserSubscription>()), Times.Never);
            _notificationService.Verify(n => n.Notify(userid, NotificationType.SubscriptionRenewed, It.IsAny<string>(), It.IsAny<string>(), null), Times.Once);
        }

        [Fact]
        public async Task HandleWebhook_InvoicePaymentFailed_SetsPastDueAndNotifiesPaymentFailed()
        {
            var userid = Guid.NewGuid();
            var subscription = new UserSubscription { userid = userid, stripe_subscription_id = "sub_1", status = SubscriptionStatus.Active };
            var evt = new PaymentWebhookEvent
            {
                stripe_event_id = "evt_inv_fail",
                event_type = "invoice.payment_failed",
                stripe_subscription_id = "sub_1",
                payment_status = PaymentStatus.Failed
            };
            _paymentGateway.Setup(g => g.ParseWebhookEvent("payload", "sig")).Returns(evt);
            _subscriptionRepository.Setup(r => r.GetUserSubscriptionByStripeSubscriptionId("sub_1")).ReturnsAsync(subscription);

            var service = CreateService();

            var response = await service.HandleWebhook("payload", "sig");

            Assert.True(response.issuccessfull);
            _subscriptionRepository.Verify(r => r.UpsertUserSubscription(It.Is<UserSubscription>(s => s.status == SubscriptionStatus.PastDue)), Times.Once);
            _featureGateService.Verify(f => f.InvalidateEffectivePlan(userid), Times.Once);
            _notificationService.Verify(n => n.Notify(userid, NotificationType.PaymentFailed, It.IsAny<string>(), It.IsAny<string>(), null), Times.Once);
        }

        [Fact]
        public async Task HandleWebhook_UnknownEventType_AcknowledgesWithoutDispatching()
        {
            var evt = new PaymentWebhookEvent { stripe_event_id = "evt_unknown", event_type = "charge.dispute.created" };
            _paymentGateway.Setup(g => g.ParseWebhookEvent("payload", "sig")).Returns(evt);

            var service = CreateService();

            var response = await service.HandleWebhook("payload", "sig");

            Assert.True(response.issuccessfull);
            Assert.Equal("ok", response.body);
            _subscriptionRepository.Verify(r => r.UpsertUserSubscription(It.IsAny<UserSubscription>()), Times.Never);
            _subscriptionRepository.Verify(r => r.CreateTransaction(It.IsAny<PaymentTransaction>()), Times.Never);
        }

        // --- GetTransactions ---

        [Fact]
        public async Task GetTransactions_ReturnsPagedResults()
        {
            var userid = Guid.NewGuid();
            var request = new TransactionPageRequest { page = 2, pagesize = 10 };
            _subscriptionRepository.Setup(r => r.GetTransactions(userid, 10, 10)).ReturnsAsync(new List<PaymentTransaction>
            {
                new PaymentTransaction { transactionid = Guid.NewGuid(), userid = userid, type = PaymentTransactionType.Subscription, amount = 9.99m, currency = "usd", status = PaymentStatus.Succeeded }
            });
            _subscriptionRepository.Setup(r => r.CountTransactions(userid)).ReturnsAsync(25);

            var service = CreateService();

            var response = await service.GetTransactions(userid, request);

            Assert.True(response.issuccessfull);
            Assert.Single(response.body!.results);
            Assert.Equal(25, response.body!.total_count);
            Assert.True(response.body!.has_more);
            Assert.Equal(2, response.body!.page);
            Assert.Equal(10, response.body!.pagesize);
        }
    }
}
