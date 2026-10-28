using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProConnect.Infrastructure.Persistence;

namespace ProConnect.WebApi.HealthChecks
{
    /// <summary>
    /// Backs the "postgres" entry under GET /health. Uses the same pooled
    /// IDbContextFactory&lt;ProConnectDbContext&gt; the repositories use (see Program.cs's
    /// AddPooledDbContextFactory remarks) rather than opening a separate raw connection, so this exercises
    /// the exact same connection pool/config the app actually relies on.
    /// </summary>
    public class PostgresHealthCheck : IHealthCheck
    {
        private readonly IDbContextFactory<ProConnectDbContext> _dbContextFactory;

        public PostgresHealthCheck(IDbContextFactory<ProConnectDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                using var dbContext = _dbContextFactory.CreateDbContext();
                var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

                return canConnect
                    ? HealthCheckResult.Healthy("Postgres connection OK")
                    : HealthCheckResult.Unhealthy("Postgres Database.CanConnectAsync() returned false");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Postgres connection failed", ex);
            }
        }
    }
}
