using ProConnect.Infrastructure.Interface;
using System;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace ProConnect.WebApi.RateLimiting
{
    /// <summary>
    /// RateLimiter subclass whose counter lives in Redis (via IRateLimiterStore) instead of in-process
    /// memory, so every horizontally-scaled instance of this API shares exactly one count per partition
    /// key - see Program.cs's AddRateLimiter wiring, which builds one of these per partition key
    /// (RateLimitPartitionKeyResolver.Resolve: userid claim, else client IP) rather than relying on the
    /// built-in RateLimitPartition.GetFixedWindowLimiter, whose counter is only ever local to one
    /// process. Instances of this class are cheap and effectively stateless - all authoritative state
    /// lives in Redis - so it doesn't matter whether PartitionedRateLimiter reuses one instance per
    /// partition key across requests or Program.cs's factory is invoked fresh each time.
    /// </summary>
    internal sealed class RedisFixedWindowRateLimiter : RateLimiter
    {
        private readonly IRateLimiterStore _store;
        private readonly string _redisKey;
        private readonly int _permitLimit;
        private readonly TimeSpan _window;

        public RedisFixedWindowRateLimiter(
            IRateLimiterStore store,
            string policyName,
            string partitionKey,
            int permitLimit,
            TimeSpan window)
        {
            _store = store;
            // Namespaced by policy as well as partition key so "global" and "strict" never share a
            // counter for the same caller - each policy has its own permit limit/window (see
            // RateLimitingConfig), so they must occupy separate Redis keys.
            _redisKey = $"ratelimit:{policyName}:{partitionKey}";
            _permitLimit = permitLimit;
            _window = window;
        }

        // Not tracked - the authoritative state is in Redis on a short-lived per-request object, not on
        // this limiter instance, so there's no meaningful local idle duration to report.
        public override TimeSpan? IdleDuration => null;

        // Not tracked for the same reason as IdleDuration above - Redis (not this object) holds the
        // real counters, and querying them just to answer GetStatistics() isn't worth an extra round
        // trip for a method nothing in this codebase currently calls.
        public override RateLimiterStatistics? GetStatistics() => null;

        protected override RateLimitLease AttemptAcquireCore(int permitCount)
        {
            // ASP.NET Core's rate limiting middleware (app.UseRateLimiter()) always goes through
            // AcquireAsync -> AcquireAsyncCore for real requests, so this synchronous path is not
            // expected to be hit in production. RateLimiter requires an implementation regardless;
            // blocking here is safe because StackExchange.Redis's async calls don't capture a
            // SynchronizationContext, so there's no deadlock risk the way there would be blocking on,
            // say, ASP.NET classic's request-bound SynchronizationContext.
            return AcquireAsyncCore(permitCount, CancellationToken.None).GetAwaiter().GetResult();
        }

        protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
        {
            // permitCount is intentionally ignored - the Redis script always increments by exactly one
            // request. ASP.NET Core's rate limiting middleware only ever calls AcquireAsync(1) per
            // request (the default), so this matches every caller this type is actually used by;
            // revisit if a policy is ever configured to request multiple permits per call.
            var result = await _store.TryAcquireAsync(_redisKey, _permitLimit, _window);
            return new RedisRateLimitLease(result.IsAcquired, result.RetryAfter);
        }
    }
}
