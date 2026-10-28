using Microsoft.Extensions.Logging;
using Moq;
using ProConnect.Application.Service;
using ProConnect.Domain.Config;
using ProConnect.Domain.Const;
using ProConnect.Domain.Model;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Repository;

namespace ProConnect.Tests.Application.Service
{
    /// <summary>
    /// Every public FeatureGateService method routes through ResolveEffectivePlan, which always goes
    /// through the concrete Cache (see FeatureGateService remarks). Unlike UserServiceTests, there's no
    /// branch that skips the cache entirely, so these tests construct a real Cache wrapping a mocked
    /// ICache (CacheAsideConfig.enablecacheaside = true) rather than passing null - the ICache mock is
    /// set up to always report a cache miss (GetCacheItem.Exists = false), so every test exercises
    /// ComputeEffectivePlan fresh against ISubscriptionRepository rather than depending on cache state
    /// carried over between tests/assertions.
    /// </summary>
    public class FeatureGateServiceTests
    {
        private readonly Mock<ISubscriptionRepository> _subscriptionRepository = new();
        private readonly Mock<ICache> _icache = new();

        public FeatureGateServiceTests()
        {
            // Unconditional cache miss - forces every ResolveEffectivePlan call to recompute from
            // ISubscriptionRepository instead of depending on cross-test cache state.
            _icache.Setup(c => c.GetCacheItem<CacheItem<EffectivePlan>>(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(default(CacheItem<CacheItem<EffectivePlan>>));
            _icache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<CacheItem<EffectivePlan>>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpirationMode>()))
                .Returns(Task.CompletedTask);
            _icache.Setup(c => c.Exists(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _icache.Setup(c => c.Delete(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        }

        private FeatureGateService CreateService()
        {
            var cache = new Cache(new Mock<ILogger<Cache>>().Object, _icache.Object, new CacheAsideConfig { enablecacheaside = true });
            var cacheExpiryConfig = new CacheExpiryConfig { effectiveplan = 600 };

            return new FeatureGateService(
                _subscriptionRepository.Object,
                cache,
                cacheExpiryConfig,
                new Mock<ILogger<FeatureGateService>>().Object);
        }

        private static SubscriptionPlan FreePlan() => new SubscriptionPlan
        {
            planid = Guid.NewGuid(),
            plan_code = PlanCode.Free,
            display_name = "Free",
            features = new List<PlanFeature>
            {
                new PlanFeature { feature_key = FeatureKey.WhoLikedMeVisible, is_enabled = false, reset_period = ResetPeriod.None },
                new PlanFeature { feature_key = FeatureKey.DailyLikes, is_enabled = true, limit_value = 10, reset_period = ResetPeriod.Daily }
            }
        };

        private static SubscriptionPlan PremiumPlan(Guid planid) => new SubscriptionPlan
        {
            planid = planid,
            plan_code = PlanCode.Premium,
            display_name = "Premium",
            features = new List<PlanFeature>
            {
                new PlanFeature { feature_key = FeatureKey.WhoLikedMeVisible, is_enabled = true, reset_period = ResetPeriod.None },
                new PlanFeature { feature_key = FeatureKey.DailyLikes, is_enabled = true, limit_value = 100, reset_period = ResetPeriod.Daily }
            }
        };

        [Fact]
        public async Task ResolveEffectivePlan_WithNoSubscriptionRow_FallsBackToFreePlan()
        {
            var userid = Guid.NewGuid();
            _subscriptionRepository.Setup(s => s.GetUserSubscription(userid)).ReturnsAsync((UserSubscription)null!);
            _subscriptionRepository.Setup(s => s.GetPlanByCode(PlanCode.Free)).ReturnsAsync(FreePlan());
            _subscriptionRepository.Setup(s => s.GetFeatureUsage(userid, FeatureKey.DailyLikes, It.IsAny<string>())).ReturnsAsync(3);

            var service = CreateService();

            var result = await service.ResolveEffectivePlan(userid);

            Assert.Equal(PlanCode.Free, result.plan_code);
            Assert.Equal("Free", result.display_name);
            Assert.True(result.features.ContainsKey(FeatureKey.DailyLikes));
            Assert.Equal(3, result.features[FeatureKey.DailyLikes].used);
            _subscriptionRepository.Verify(s => s.GetPlanById(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task ResolveEffectivePlan_WithLapsedSubscription_FallsBackToFreePlan()
        {
            var userid = Guid.NewGuid();
            var planid = Guid.NewGuid();
            _subscriptionRepository.Setup(s => s.GetUserSubscription(userid))
                .ReturnsAsync(new UserSubscription { userid = userid, planid = planid, status = SubscriptionStatus.Canceled });
            _subscriptionRepository.Setup(s => s.GetPlanByCode(PlanCode.Free)).ReturnsAsync(FreePlan());
            _subscriptionRepository.Setup(s => s.GetFeatureUsage(userid, FeatureKey.DailyLikes, It.IsAny<string>())).ReturnsAsync(0);

            var service = CreateService();

            var result = await service.ResolveEffectivePlan(userid);

            Assert.Equal(PlanCode.Free, result.plan_code);
            _subscriptionRepository.Verify(s => s.GetPlanById(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task ResolveEffectivePlan_WithActiveSubscription_ResolvesThatPlan()
        {
            var userid = Guid.NewGuid();
            var planid = Guid.NewGuid();
            _subscriptionRepository.Setup(s => s.GetUserSubscription(userid))
                .ReturnsAsync(new UserSubscription { userid = userid, planid = planid, status = SubscriptionStatus.Active });
            _subscriptionRepository.Setup(s => s.GetPlanById(planid)).ReturnsAsync(PremiumPlan(planid));
            _subscriptionRepository.Setup(s => s.GetFeatureUsage(userid, FeatureKey.DailyLikes, It.IsAny<string>())).ReturnsAsync(5);

            var service = CreateService();

            var result = await service.ResolveEffectivePlan(userid);

            Assert.Equal(PlanCode.Premium, result.plan_code);
            Assert.True(result.features[FeatureKey.WhoLikedMeVisible].is_enabled);
            _subscriptionRepository.Verify(s => s.GetPlanByCode(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task IsEnabled_WhenFeatureIsEnabledOnPlan_ReturnsTrue()
        {
            var userid = Guid.NewGuid();
            var planid = Guid.NewGuid();
            _subscriptionRepository.Setup(s => s.GetUserSubscription(userid))
                .ReturnsAsync(new UserSubscription { userid = userid, planid = planid, status = SubscriptionStatus.Active });
            _subscriptionRepository.Setup(s => s.GetPlanById(planid)).ReturnsAsync(PremiumPlan(planid));
            _subscriptionRepository.Setup(s => s.GetFeatureUsage(userid, FeatureKey.DailyLikes, It.IsAny<string>())).ReturnsAsync(0);

            var service = CreateService();

            var enabled = await service.IsEnabled(userid, FeatureKey.WhoLikedMeVisible);

            Assert.True(enabled);
        }

        [Fact]
        public async Task IsEnabled_WhenFeatureIsNotOnPlanAtAll_ReturnsFalse()
        {
            var userid = Guid.NewGuid();
            _subscriptionRepository.Setup(s => s.GetUserSubscription(userid)).ReturnsAsync((UserSubscription)null!);
            _subscriptionRepository.Setup(s => s.GetPlanByCode(PlanCode.Free)).ReturnsAsync(FreePlan());
            _subscriptionRepository.Setup(s => s.GetFeatureUsage(userid, FeatureKey.DailyLikes, It.IsAny<string>())).ReturnsAsync(0);

            var service = CreateService();

            var enabled = await service.IsEnabled(userid, FeatureKey.WhoLikedMeVisible);

            Assert.False(enabled);
        }

        [Fact]
        public async Task TryConsume_WhenFeatureNotEnabled_ReturnsNotAllowedWithoutIncrementing()
        {
            var userid = Guid.NewGuid();
            _subscriptionRepository.Setup(s => s.GetUserSubscription(userid)).ReturnsAsync((UserSubscription)null!);
            _subscriptionRepository.Setup(s => s.GetPlanByCode(PlanCode.Free)).ReturnsAsync(FreePlan());
            _subscriptionRepository.Setup(s => s.GetFeatureUsage(userid, FeatureKey.DailyLikes, It.IsAny<string>())).ReturnsAsync(0);

            var service = CreateService();

            var result = await service.TryConsume(userid, FeatureKey.WhoLikedMeVisible);

            Assert.False(result.allowed);
            Assert.Null(result.limit);
            Assert.Equal(0, result.used);
            _subscriptionRepository.Verify(s => s.TryIncrementFeatureUsage(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>()), Times.Never);
        }

        [Fact]
        public async Task TryConsume_UnderLimit_IncrementsAndReturnsAllowed()
        {
            var userid = Guid.NewGuid();
            _subscriptionRepository.Setup(s => s.GetUserSubscription(userid)).ReturnsAsync((UserSubscription)null!);
            _subscriptionRepository.Setup(s => s.GetPlanByCode(PlanCode.Free)).ReturnsAsync(FreePlan());
            _subscriptionRepository.Setup(s => s.GetFeatureUsage(userid, FeatureKey.DailyLikes, It.IsAny<string>())).ReturnsAsync(3);
            _subscriptionRepository.Setup(s => s.TryIncrementFeatureUsage(userid, FeatureKey.DailyLikes, It.IsAny<string>(), 1, 10))
                .ReturnsAsync(4);

            var service = CreateService();

            var result = await service.TryConsume(userid, FeatureKey.DailyLikes);

            Assert.True(result.allowed);
            Assert.Equal(10, result.limit);
            Assert.Equal(4, result.used);
        }

        [Fact]
        public async Task TryConsume_AtLimit_RejectsAndReportsCurrentUsage()
        {
            var userid = Guid.NewGuid();
            _subscriptionRepository.Setup(s => s.GetUserSubscription(userid)).ReturnsAsync((UserSubscription)null!);
            _subscriptionRepository.Setup(s => s.GetPlanByCode(PlanCode.Free)).ReturnsAsync(FreePlan());
            _subscriptionRepository.Setup(s => s.GetFeatureUsage(userid, FeatureKey.DailyLikes, It.IsAny<string>())).ReturnsAsync(10);
            _subscriptionRepository.Setup(s => s.TryIncrementFeatureUsage(userid, FeatureKey.DailyLikes, It.IsAny<string>(), 1, 10))
                .ReturnsAsync((int?)null);

            var service = CreateService();

            var result = await service.TryConsume(userid, FeatureKey.DailyLikes);

            Assert.False(result.allowed);
            Assert.Equal(10, result.limit);
            Assert.Equal(10, result.used);
        }

        [Fact]
        public async Task InvalidateEffectivePlan_DeletesTheCachedKey()
        {
            var userid = Guid.NewGuid();

            var service = CreateService();

            await service.InvalidateEffectivePlan(userid);

            _icache.Verify(c => c.Delete($"effectiveplan:{userid}", CacheRegion.EFFECTIVEPLAN), Times.Once);
        }

        [Fact]
        public async Task RefundConsumption_ForCountedFeature_CreditsBackNegativeAmount()
        {
            var userid = Guid.NewGuid();
            _subscriptionRepository.Setup(s => s.GetUserSubscription(userid)).ReturnsAsync((UserSubscription)null!);
            _subscriptionRepository.Setup(s => s.GetPlanByCode(PlanCode.Free)).ReturnsAsync(FreePlan());
            _subscriptionRepository.Setup(s => s.GetFeatureUsage(userid, FeatureKey.DailyLikes, It.IsAny<string>())).ReturnsAsync(4);

            var service = CreateService();

            await service.RefundConsumption(userid, FeatureKey.DailyLikes);

            _subscriptionRepository.Verify(s => s.IncrementFeatureUsage(userid, FeatureKey.DailyLikes, It.IsAny<string>(), -1), Times.Once);
        }

        [Fact]
        public async Task RefundConsumption_ForPlainFlagFeature_IsNoOp()
        {
            var userid = Guid.NewGuid();
            _subscriptionRepository.Setup(s => s.GetUserSubscription(userid)).ReturnsAsync((UserSubscription)null!);
            _subscriptionRepository.Setup(s => s.GetPlanByCode(PlanCode.Free)).ReturnsAsync(FreePlan());
            _subscriptionRepository.Setup(s => s.GetFeatureUsage(userid, FeatureKey.DailyLikes, It.IsAny<string>())).ReturnsAsync(0);

            var service = CreateService();

            await service.RefundConsumption(userid, FeatureKey.WhoLikedMeVisible);

            _subscriptionRepository.Verify(s => s.IncrementFeatureUsage(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }
    }
}
