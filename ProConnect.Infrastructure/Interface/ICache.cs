using ProConnect.Domain.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Interface
{
    public interface ICache
    {
        CacheConfig CacheConfig { get; }
        Task Flush(string region = null);
        Task Delete(string key, string region = null);
        Task<T> Get<T>(string key, string region = null);
        Task Set<T>(string key, T value, string region = null, TimeSpan? expiry = null, ExpirationMode expirationMode = ExpirationMode.Default);
        Task<bool> Exists(string key, string region = null);
        Task<CacheItem<T>> GetCacheItem<T>(string key, string region = null);
    }
}
