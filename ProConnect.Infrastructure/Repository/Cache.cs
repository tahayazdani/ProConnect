using Microsoft.Extensions.Logging;
using ProConnect.Domain.Config;
using ProConnect.Infrastructure.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Repository
{
    public class Cache
    {
        private readonly ILogger<Cache> _logger;
        private readonly ICache _cache;
        private readonly CacheAsideConfig _config;

        public Cache(
           ILogger<Cache> logger,
           ICache cache,
           CacheAsideConfig config
       )
        {
            _logger = logger;
            _cache = cache;
            _config = config;
        }

        public async Task<T> Get<T>(string key, Func<Task<T>> getFromOrigin, string region = null) where T : class, new()
        {
            try
            {
                var cachedItem = await _cache.GetCacheItem<CacheItem<T>>(key: key, region: region);
                var nestedCachedItem = cachedItem.Value;
                var foundInCache = cachedItem.Exists;
                var expiredInCache = false;
                T data = default;

                if (foundInCache)
                {
                    _logger.LogDebug($"Cache hit with key: '{key}");
                    data = nestedCachedItem.Value;
                    expiredInCache = (DateTime.UtcNow > nestedCachedItem.ExpiresOn);

                    if (expiredInCache)
                        _logger.LogDebug($"Cache expired for the key: '{key}");
                }
                else
                {
                    _logger.LogDebug($"Cache miss with key: '{key}");
                }

                if ((!foundInCache) || (expiredInCache))
                {
                    try
                    {
                        data = await getFromOrigin();
                        if (data != null)
                        {
                            await Set(key: key, data: data, region: region);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error getting object from origin, exception: {ex}");
                        if ((data == null) || (!_config.enablecacheaside))
                        {
                            _logger.LogError($"No data to return for the key: {key}");
                            throw;
                        }
                    }
                }
                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting value: '{key}', region: {region}, exception: {ex}");
                throw;
            }
        }

        public async Task<T> Get<T>(string key, string region = null) where T : class, new()
        {
            try
            {
                var cachedItem = await _cache.GetCacheItem<CacheItem<T>>(key: key, region: region);

                var foundInCache = cachedItem.Exists;
                var expiredInCache = false;
                T data = default;

                if (foundInCache)
                {
                    _logger.LogDebug($"Cache hit with key: '{key}");
                    var nestedCachedItem = cachedItem.Value;
                    data = nestedCachedItem.Value;
                    expiredInCache = (DateTime.UtcNow > nestedCachedItem.ExpiresOn);

                    if (expiredInCache) _logger.LogDebug($"Cache expired for the key: '{key}");

                    return data;
                }

                _logger.LogDebug($"Cache miss with key: '{key}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting value: '{key}', region: {region}, exception: {ex}");
                throw;
            }
        }

        public async Task Set<T>(string key, T data, string region = null)
        {
            try
            {
                if (_config.enablecacheaside && data != null)
                {
                    // Defines when to retrieve data from the origin assuming the origin is available
                    var expiresOn = DateTime.UtcNow + TimeSpan.FromSeconds(_config.timeout);
                    var cacheItem = new CacheItem<T>
                    {
                        ExpiresOn = expiresOn,
                        Value = data,
                        Key = key,
                        Region = region,
                        Ttl = _config.cacheasidetimeout
                    };

                    // Defines when to remove data from cache permanently 
                    var cacheAsideExpires = TimeSpan.FromSeconds(_config.cacheasidetimeout);
                    await _cache.Set(key: key, cacheItem, region: region, cacheAsideExpires);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error storing object in cache, key: '{key}', region: '{region}', value: '{data}', exception: {ex}");
            }
        }

        public async Task Delete(string key, string region = null)
        {
            try
            {
                if (_config.enablecacheaside && (await _cache.Exists(key, region)))
                {
                    await _cache.Delete(key: key, region: region);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error deleting object from cache, key: '{key}', region: '{region}', exception: {ex}");
            }
        }

        public async Task Flush()
        {
            await _cache.Flush();
        }

    }
}
