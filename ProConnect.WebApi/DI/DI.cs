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

        public static CacheConfig GetCacheConfiguration(this IConfigurationRoot configurationRoot, string sectionName = "CacheConfig")
        {
            var configurationSection = configurationRoot.GetSection(sectionName);
            var cacheConfig = new CacheConfig()
            {
                cachetype = GetConfigValue(configurationSection, "CacheType"),
                expirationmode = Enum.Parse<ExpirationMode>(GetConfigValue(configurationSection, "ExpirationMode")),
                timeout = int.Parse(GetConfigValue(configurationSection, "Timeout", "0")),
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

        public static CacheAsideConfig GetCacheAsideConfiguration(this IConfigurationRoot configurationRoot, string sectionName = "CacheAsideConfig")
        {
            var configurationSection = configurationRoot.GetSection(sectionName);
            var cacheAsideConfig = new CacheAsideConfig
            {
                enablecacheaside = bool.Parse(GetConfigValue(configurationSection, "ConnectionString", "true")),
                timeout = int.Parse(GetConfigValue(configurationSection, "Timeout", "300")), // 5 min
                cacheasidetimeout = int.Parse(GetConfigValue(configurationSection, "CacheAsideTimeout", "86400")) // 24 hrs
            };
            return cacheAsideConfig;
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
