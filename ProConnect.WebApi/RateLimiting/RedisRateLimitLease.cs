using System;
using System.Collections.Generic;
using System.Threading.RateLimiting;

namespace ProConnect.WebApi.RateLimiting
{
    /// <summary>
    /// The lease RedisFixedWindowRateLimiter hands back. Only ever exposes MetadataName.RetryAfter, and
    /// only when the lease was rejected (RetryAfter is null on an acquired lease) - ASP.NET Core's
    /// rate limiting middleware reads that metadata to populate the standard Retry-After response
    /// header on a 429.
    /// </summary>
    internal sealed class RedisRateLimitLease : RateLimitLease
    {
        private readonly TimeSpan? _retryAfter;

        public RedisRateLimitLease(bool isAcquired, TimeSpan? retryAfter)
        {
            IsAcquired = isAcquired;
            _retryAfter = retryAfter;
        }

        public override bool IsAcquired { get; }

        public override IEnumerable<string> MetadataNames =>
            _retryAfter.HasValue ? new[] { MetadataName.RetryAfter.Name } : Array.Empty<string>();

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (_retryAfter.HasValue && metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = _retryAfter.Value;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}
