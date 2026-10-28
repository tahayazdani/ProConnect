using ProConnect.WebApi.RateLimiting;
using System;
using System.Threading.RateLimiting;

namespace ProConnect.Tests.WebApi.RateLimiting
{
    /// <summary>
    /// RedisRateLimitLease is internal - testable via InternalsVisibleTo. It only ever advertises
    /// MetadataName.RetryAfter, and only when constructed with a non-null retryAfter (the rejection case) -
    /// an acquired lease has none, matching how RedisFixedWindowRateLimiter.AcquireAsyncCore always passes
    /// null for retryAfter on the acquired path.
    /// </summary>
    public class RedisRateLimitLeaseTests
    {
        [Fact]
        public void AcquiredLease_WithNoRetryAfter_HasNoMetadata()
        {
            var lease = new RedisRateLimitLease(isAcquired: true, retryAfter: null);

            Assert.True(lease.IsAcquired);
            Assert.Empty(lease.MetadataNames);
            Assert.False(lease.TryGetMetadata(MetadataName.RetryAfter.Name, out var metadata));
            Assert.Null(metadata);
        }

        [Fact]
        public void RejectedLease_WithRetryAfter_ExposesRetryAfterMetadata()
        {
            var retryAfter = TimeSpan.FromSeconds(42);
            var lease = new RedisRateLimitLease(isAcquired: false, retryAfter: retryAfter);

            Assert.False(lease.IsAcquired);
            Assert.Contains(MetadataName.RetryAfter.Name, lease.MetadataNames);

            var found = lease.TryGetMetadata(MetadataName.RetryAfter.Name, out var metadata);

            Assert.True(found);
            Assert.Equal(retryAfter, metadata);
        }

        [Fact]
        public void TryGetMetadata_WithUnknownMetadataName_ReturnsFalse()
        {
            var lease = new RedisRateLimitLease(isAcquired: false, retryAfter: TimeSpan.FromSeconds(10));

            var found = lease.TryGetMetadata("some-other-metadata", out var metadata);

            Assert.False(found);
            Assert.Null(metadata);
        }
    }
}
