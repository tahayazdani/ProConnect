using ProConnect.Domain.Config;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Repository;

namespace ProConnect.WebApi.DI
{
    public static class DI
    {
        public static void AddCaching(this IServiceCollection services, CacheConfig cacheConfig)
        {
            services.AddSingleton(cacheConfig);
            switch (cacheConfig.cachetype.ToLowerInvariant())
            {
                case "redis":
                    services.AddSingleton<ICache, RedisCache>();
                    break;
                default:
                    throw new Exception($"Unknown cacheType: {cacheConfig.cachetype}");
            }
        }

        public static CacheConfig GetCacheConfiguration(this IConfigurationRoot configurationRoot, string sectionName = "Cache:Redis")
        {
            var configurationSection = configurationRoot.GetSection(sectionName);
            var cacheConfig = new CacheConfig()
            {
                cachetype = GetConfigValue(configurationSection, "CacheType"),
                expirationmode = Enum.Parse<ExpirationMode>(GetConfigValue(configurationSection, "ExpirationMode")),
                connectionstring = GetConfigValue(configurationSection, "ConnectionString"),
                databaseindex = int.Parse(GetConfigValue(configurationSection, "DatabaseIndex", "0"))
            };
            return cacheConfig;
        }

        public static void AddCacheAside(this IServiceCollection services, CacheAsideConfig cacheConfig)
        {
            services.AddSingleton(cacheConfig);
            services.AddSingleton<Cache, Cache>();
        }

        public static CacheAsideConfig GetCacheAsideConfiguration(this IConfigurationRoot configurationRoot, string sectionName = "Cache:CacheAside")
        {
            var configurationSection = configurationRoot.GetSection(sectionName);
            var cacheAsideConfig = new CacheAsideConfig
            {
                enablecacheaside = bool.Parse(GetConfigValue(configurationSection, "EnableCacheAside", "true"))
            };
            return cacheAsideConfig;
        }
        public static void AddCacheExpiry(this IServiceCollection services, CacheExpiryConfig cacheExpiryConfig)
        {
            services.AddSingleton(cacheExpiryConfig);
        }

        public static CacheExpiryConfig GetCacheExpiryConfiguration(this IConfigurationRoot configurationRoot, string sectionName = "Cache:Expiry")
        {
            var configurationSection = configurationRoot.GetSection(sectionName);
            var cacheExpiryConfig = new CacheExpiryConfig
            {
                personprofile = int.Parse(GetConfigValue(configurationSection, "PersonProfile", "604800")), // 7 days
                matchrecommendation = int.Parse(GetConfigValue(configurationSection, "MatchRecommendation", "900")), // 15 min
                searchresult = int.Parse(GetConfigValue(configurationSection, "SearchResult", "300")), // 5 min
                geocode = int.Parse(GetConfigValue(configurationSection, "Geocode", "7776000")), // 90 days
                userprofile = int.Parse(GetConfigValue(configurationSection, "UserProfile", "600")), // 10 min
                effectiveplan = int.Parse(GetConfigValue(configurationSection, "EffectivePlan", "600")) // 10 min
            };
            return cacheExpiryConfig;
        }

        public static string GetConfigValue(
                    this IConfigurationSection section,
                    string key,
                    string defaultValue = ""
                )
        {
            var configElement = section.GetChildren()
                .FirstOrDefault(
                    c => c.Key.Equals(
                        key, System.StringComparison.InvariantCultureIgnoreCase
                    )
                );

            return configElement != null ? configElement.Value : defaultValue;
        }

        public static TTarget CopyObject<TTarget>(this object source) where TTarget : new()
        {
            var target = new TTarget();
            var sourceProperties = source.GetType().GetProperties().Where(x => x.CanRead).ToList();
            var targetProperties = typeof(TTarget).GetProperties().Where(x => x.CanWrite);

            sourceProperties.ForEach(sourceProperty =>
            {
                if (targetProperties.Any(x => x.Name == sourceProperty.Name))
                {
                    var p = targetProperties.First(x => x.Name == sourceProperty.Name);
                    p.SetValue(target, sourceProperty.GetValue(source, null), null);
                }
            });

            return target;
        }
    }
}
