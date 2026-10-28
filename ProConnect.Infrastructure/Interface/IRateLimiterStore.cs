using System;

namespace ProConnect.Infrastructure.Interface
{
    /// <summary>
    /// Centralized, cross-instance fixed-window counter for rate limiting. Unlike ICache (which is a
    /// generic get/set cache), this interface exists specifically so the counter increment-and-check is
    /// one atomic operation - see RedisRateLimiterStore's Lua script - which a plain Get-then-Set over
    /// ICache could never guarantee under concurrent callers, whether on one instance or many.
    /// </summary>
    public interface IRateLimiterStore
    {
        /// <summary>
        /// Atomically increments the counter for <paramref name="key"/> and reports whether the caller
        /// is still within <paramref name="permitLimit"/> for the current fixed window of length
        /// <paramref name="window"/>. Implementations are expected to fail open (return an acquired
        /// result) rather than throw if the backing store is unavailable - callers should not need their
        /// own fallback logic.
        /// </summary>
        Task<RateLimitAcquireResult> TryAcquireAsync(string key, int permitLimit, TimeSpan window);
    }

    /// <summary>
    /// Outcome of one TryAcquireAsync call. RetryAfter is only meaningful (non-null) when IsAcquired is
    /// false - it feeds MetadataName.RetryAfter on the rejection lease so ASP.NET Core's rate limiting
    /// middleware can populate the standard Retry-After response header.
    /// </summary>
    public readonly struct RateLimitAcquireResult
    {
        public RateLimitAcquireResult(bool isAcquired, TimeSpan? retryAfter)
        {
            IsAcquired = isAcquired;
            RetryAfter = retryAfter;
        }

        public bool IsAcquired { get; }
        public TimeSpan? RetryAfter { get; }

        public static RateLimitAcquireResult Acquired { get; } = new RateLimitAcquireResult(true, null);
    }
}
