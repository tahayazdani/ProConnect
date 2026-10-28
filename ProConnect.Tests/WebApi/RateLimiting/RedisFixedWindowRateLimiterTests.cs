using Moq;
using ProConnect.Infrastructure.Interface;
using ProConnect.WebApi.RateLimiting;
using System;
using System.Threading.RateLimiting;

namespace ProConnect.Tests.WebApi.RateLimiting
{
    /// <summary>
    /// RedisFixedWindowRateLimiter is internal - testable via InternalsVisibleTo. RateLimiter's public
    /// AcquireAsync/AttemptAcquire both just forward to the protected AcquireAsyncCore/AttemptAcquireCore
    /// this class overrides, so they can be exercised directly through the public API without reflection.
    /// IRateLimiterStore is mocked - the real Redis-backed store (RedisRateLimiterStore) is out of scope
    /// here since it needs a live Redis connection.
    /// </summary>
    public class RedisFixedWindowRateLimiterTests
    {
        private readonly Mock<IRateLimiterStore> _store = new();

        [Fact]
        public async Task AcquireAsync_BuildsNamespacedRedisKeyFromPolicyAndPartition()
        {
            string? capturedKey = null;
            _store.Setup(s => s.TryAcquireAsync(It.IsAny<string>(), 5, TimeSpan.FromSeconds(60)))
                .Callback<string, int, TimeSpan>((key, _, __) => capturedKey = key)
                .ReturnsAsync(RateLimitAcquireResult.Acquired);

            var limiter = new RedisFixedWindowRateLimiter(_store.Object, "strict", "user:42", 5, TimeSpan.FromSeconds(60));

            await limiter.AcquireAsync(1);

            Assert.Equal("ratelimit:strict:user:42", capturedKey);
        }

        [Fact]
        public async Task AcquireAsync_WhenStoreAcquires_ReturnsAcquiredLeaseWithNoRetryAfter()
        {
            _store.Setup(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(RateLimitAcquireResult.Acquired);

            var limiter = new RedisFixedWindowRateLimiter(_store.Object, "global", "ip:1.2.3.4", 100, TimeSpan.FromSeconds(60));

            var lease = await limiter.AcquireAsync(1);

            Assert.True(lease.IsAcquired);
            Assert.Empty(lease.MetadataNames);
        }

        [Fact]
        public async Task AcquireAsync_WhenStoreRejects_ReturnsRejectedLeaseWithRetryAfterMetadata()
        {
            var retryAfter = TimeSpan.FromSeconds(17);
            _store.Setup(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(new RateLimitAcquireResult(false, retryAfter));

            var limiter = new RedisFixedWindowRateLimiter(_store.Object, "strict", "user:42", 5, TimeSpan.FromSeconds(60));

            var lease = await limiter.AcquireAsync(1);

            Assert.False(lease.IsAcquired);
            var found = lease.TryGetMetadata(MetadataName.RetryAfter.Name, out var metadata);
            Assert.True(found);
            Assert.Equal(retryAfter, metadata);
        }

        [Fact]
        public void AttemptAcquire_SynchronousPath_StillDelegatesToStoreAndReturnsLease()
        {
            _store.Setup(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(RateLimitAcquireResult.Acquired);

            var limiter = new RedisFixedWindowRateLimiter(_store.Object, "global", "ip:5.6.7.8", 100, TimeSpan.FromSeconds(60));

            var lease = limiter.AttemptAcquire(1);

            Assert.True(lease.IsAcquired);
            _store.Verify(s => s.TryAcquireAsync("ratelimit:global:ip:5.6.7.8", 100, TimeSpan.FromSeconds(60)), Times.Once);
        }

        [Fact]
        public void IdleDurationAndGetStatistics_AreNotTracked()
        {
            var limiter = new RedisFixedWindowRateLimiter(_store.Object, "global", "ip:9.9.9.9", 100, TimeSpan.FromSeconds(60));

            Assert.Null(limiter.IdleDuration);
            Assert.Null(limiter.GetStatistics());
        }
    }
}
