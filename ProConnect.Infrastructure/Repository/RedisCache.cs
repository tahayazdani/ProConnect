using Microsoft.Extensions.Logging;
using ProConnect.Domain.Config;
using ProConnect.Infrastructure.Interface;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Repository
{
    public class RedisCache : BaseCache, ICache
    {
        private readonly object _initializerLock = new object();

        private static bool _initialized = false;
        private static IDatabase _redisConnection;

        public RedisCache(CacheConfig cacheConfig, ILogger<RedisCache> logger = null)
            : base(cacheConfig: cacheConfig, logger)
        {
            if (logger != null)
            {
                logger.LogDebug($"Redis with cacheConfig at {DateTime.Now.ToLongTimeString()}");
            }
            // Initialize single-tone instance to support single instance connection
            // and dependency injection for single-tone and scoped alike
            if (_initialized)
            {
                return;
            }
            else
            {
                // Block concurrent initialization
                lock (_initializerLock)
                {
                    if (!_initialized)
                    {
                        _initialized = InitializeCache(cacheConfig).Result;
                    }
                }
            }
        }

        private static async Task<bool> InitializeCache(
            CacheConfig cacheConfig
        )
        {
            try
            {
                var logMsg = $"Redis.InitializeCache called";
                Console.WriteLine(logMsg);

                var connection = await ConnectionMultiplexer.ConnectAsync(cacheConfig.connectionstring);
                _redisConnection = connection.GetDatabase(cacheConfig.databaseindex);
                return _redisConnection.IsConnected("Ping");
            }
            catch (Exception)
            {
                throw;
            }
        }

        public async Task Set<T>(
            string key,
            T value,
            string region = null,
            TimeSpan? expiry = null,
            ExpirationMode expirationMode = ExpirationMode.Default
        )
        {
            try
            {
                var hashedKey = this.GetKeyHash(key, region);
                // No global fallback TTL anymore (removed CacheConfig.Timeout) - every caller is expected
                // to supply a region-appropriate expiry, sourced from CacheExpiryConfig via the Cache
                // cache-aside wrapper, which guards this the same way before it ever reaches here.
                var localExpiry = expiry ?? throw new ArgumentException(
                    "A cache expiry is required - callers must supply a region-appropriate TTL (see CacheExpiryConfig).", nameof(expiry));
                var ttl = localExpiry.TotalSeconds;

                var cacheItem = new CacheItem<T>()
                {
                    Key = key,
                    Region = region,
                    Ttl = ttl,
                    ExpirationMode = (expirationMode == ExpirationMode.Default ?
                                        this.CacheConfig.expirationmode :
                                        expirationMode),
                    Value = value
                };

                var rawValue = this.Serialize(cacheItem);

                await _redisConnection.StringSetAsync(
                    hashedKey,
                    rawValue,
                    localExpiry
                );
                //await _redisConnection.KeyExpireAsync(hashedKey, localExpiry);
            }
            catch (Exception ex)
            {
                var msg = $"Error setting value '{value}' with key '{key}', error: {ex}";
                LogError(msg, ex);
                throw;
            }
        }

        public async Task<CacheItem<T>> GetCacheItem<T>(string key, string region = null)
        {
            try
            {
                var hashedKey = this.GetKeyHash(key, region);
                CacheItem<T> cacheItem = default;
                var rawValue = await _redisConnection.StringGetAsync(hashedKey);

                if (rawValue.HasValue)
                {
                    cacheItem = this.Deserialize<CacheItem<T>>(rawValue);
                    cacheItem.Exists = true;

                    if (cacheItem.ExpirationMode == ExpirationMode.Sliding)
                    {
                        // Fire and forget
                        _ = _redisConnection.KeyExpireAsync(
                            hashedKey, TimeSpan.FromSeconds(cacheItem.Ttl)
                        );
                    }
                }

                return cacheItem;
            }
            catch (Exception ex)
            {
                var msg = $"Error getting value with key '{key}', error: {ex}";
                LogError(msg, ex);
                throw;
            }
        }

        public async Task<T> Get<T>(string key, string region = null)
        {
            return (await this.GetCacheItem<T>(key, region)).Value;
        }

        public async Task Delete(string key, string region = null)
        {
            try
            {
                var hashedKey = this.GetKeyHash(key, region);
                await _redisConnection.KeyDeleteAsync(hashedKey);
            }
            catch (Exception ex)
            {
                var msg = $"Error deleting value with key '{key}', error: {ex}";
                LogError(msg, ex);
                throw;
            }
        }

        public async Task<bool> Exists(string key, string region = null)
        {
            var hashedKey = GetKeyHash(key, region);
            return await _redisConnection.KeyExistsAsync(hashedKey);
        }
    }
}
