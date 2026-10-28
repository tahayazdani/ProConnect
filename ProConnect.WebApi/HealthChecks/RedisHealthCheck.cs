using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProConnect.Infrastructure.Interface;

namespace ProConnect.WebApi.HealthChecks
{
    /// <summary>
    /// Backs the "redis" entry under GET /health. ICache has no dedicated Ping - Exists() against a
    /// throwaway key is used instead purely to exercise the live connection; whether the probe key
    /// happens to exist is irrelevant, only that asking didn't throw/time out.
    /// </summary>
    public class RedisHealthCheck : IHealthCheck
    {
        private const string ProbeKey = "healthcheck:probe";

        private readonly ICache _cache;

        public RedisHealthCheck(ICache cache)
        {
            _cache = cache;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                await _cache.Exists(ProbeKey);
                return HealthCheckResult.Healthy("Redis connection OK");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Redis connection failed", ex);
            }
        }
    }
}
