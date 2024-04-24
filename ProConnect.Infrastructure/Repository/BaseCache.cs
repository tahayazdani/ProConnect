using Microsoft.Extensions.Logging;
using ProConnect.Domain.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Repository
{
    public abstract class BaseCache
    {
        private readonly JsonSerializerOptions _defaultOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        public CacheConfig CacheConfig { get; private set; }
        protected ILogger<BaseCache> Logger { get; set; }

        public BaseCache(CacheConfig cacheConfig, ILogger<BaseCache> logger)
        {
            CacheConfig = cacheConfig;
            Logger = logger;
        }

        protected TimeSpan GetDefaultExpiry() => TimeSpan.FromSeconds(this.CacheConfig.timeout);

        protected virtual string Serialize(
            object value
        )
        {
            string result = JsonSerializer.Serialize(value, value.GetType(), _defaultOptions);
            return result;
        }

        protected virtual T Deserialize<T>(
            string value
        )
        {
            T result = JsonSerializer.Deserialize<T>(
                value,
                _defaultOptions
                );
            return result;
        }

        protected virtual string GetKeyHash(string key, string region = null)
        {
            var hashedString = GetHashString(key);
            if (!string.IsNullOrEmpty(region))
            {
                hashedString = $"{region}:{hashedString}";
            }
            return hashedString;
        }

        protected virtual byte[] GetHash(string inputString)
        {
            using (HashAlgorithm algorithm = SHA256.Create())
                return algorithm.ComputeHash(Encoding.UTF8.GetBytes(inputString));
        }

        protected virtual string GetHashString(string inputString)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in GetHash(inputString))
                sb.Append(b.ToString("X2"));

            return sb.ToString();
        }

        public virtual Task Flush(string region = null) => throw new NotImplementedException("https://tqdev.com/2016-active-cache-invalidation-anti-pattern");

        protected void LogError(string msg, Exception ex)
        {
            if (Logger != null)
                Logger.LogError(msg);
            else
                Console.WriteLine(msg);
        }
    }
}
