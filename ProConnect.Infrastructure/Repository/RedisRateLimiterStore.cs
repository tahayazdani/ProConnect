using Microsoft.Extensions.Logging;
using ProConnect.Infrastructure.Interface;
using StackExchange.Redis;
using System;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Repository
{
    /// <summary>
    /// IRateLimiterStore backed by Redis instead of in-process memory, so every horizontally-scaled
    /// instance of this API shares exactly one counter per partition key (per-user or per-IP - see
    /// RateLimitPartitionKeyResolver) rather than each instance enforcing its own independent limit, as
    /// the previous PartitionedRateLimiter.Create(...) + RateLimitPartition.GetFixedWindowLimiter(...)
    /// in-memory setup effectively did.
    ///
    /// Deliberately a separate Redis connection/IDatabase from RedisCache's - RedisCache keeps its
    /// IDatabase private/static to that one class, and this store's access pattern (EVAL of a fixed
    /// script, no serialization) is different enough from the generic cache-aside Get/Set/Delete surface
    /// that reusing ICache would mean either leaking EVAL through ICache's interface (wrong abstraction)
    /// or going through an extra hop. The connection itself still points at the same Redis instance/
    /// database index as the cache, read from the same CacheConfig (see Program.cs's IRateLimiterStore
    /// DI registration).
    /// </summary>
    public class RedisRateLimiterStore : IRateLimiterStore
    {
        private readonly IDatabase _database;
        private readonly ILogger<RedisRateLimiterStore> _logger;

        // Atomic INCR + conditional PEXPIRE, evaluated on Redis's single command-processing thread - this
        // is what makes the fixed-window counter race-free across every concurrently-calling instance of
        // this API, not just within one process (an in-process lock/ConcurrentDictionary only ever
        // covered one process). PEXPIRE only fires the first time a window is touched (current == 1), so
        // the TTL isn't pushed out by every later request in that window - that's what makes this a FIXED
        // window (resets on a wall-clock cadence from first use) rather than a sliding one.
        private const string IncrementAndExpireScript = @"
            local current = redis.call('INCR', KEYS[1])
            if current == 1 then
                redis.call('PEXPIRE', KEYS[1], ARGV[1])
            end
            local ttl = redis.call('PTTL', KEYS[1])
            return { current, ttl }
        ";

        public RedisRateLimiterStore(IDatabase database, ILogger<RedisRateLimiterStore> logger)
        {
            _database = database;
            _logger = logger;
        }

        public async Task<RateLimitAcquireResult> TryAcquireAsync(string key, int permitLimit, TimeSpan window)
        {
            try
            {
                var rawResult = await _database.ScriptEvaluateAsync(
                    IncrementAndExpireScript,
                    new RedisKey[] { key },
                    new RedisValue[] { (long)window.TotalMilliseconds });

                var values = (RedisResult[])rawResult!;
                var current = (long)values[0];
                var ttlMilliseconds = (long)values[1];

                if (current <= permitLimit)
                {
                    return RateLimitAcquireResult.Acquired;
                }

                // ttlMilliseconds can come back -1 in a narrow race (PTTL read before the PEXPIRE from
                // the request that set current == 1 has landed) - treat that as "assume a full window
                // remains" rather than surfacing a meaningless negative/zero Retry-After to the client.
                var retryAfter = ttlMilliseconds >= 0
                    ? TimeSpan.FromMilliseconds(ttlMilliseconds)
                    : window;

                return new RateLimitAcquireResult(isAcquired: false, retryAfter);
            }
            catch (Exception ex)
            {
                // Fail-open: Redis being unreachable should degrade to "rate limiting is temporarily
                // off", not "the whole API starts rejecting or erroring for every caller" - mirrors
                // Cache's cache-aside philosophy (Infrastructure/Repository/Cache.cs), which already
                // treats Redis as best-effort infrastructure the request path survives without.
                _logger.LogError(ex, "Rate limiter store unavailable for key '{Key}' - failing open (request allowed).", key);
                return RateLimitAcquireResult.Acquired;
            }
        }
    }
}
