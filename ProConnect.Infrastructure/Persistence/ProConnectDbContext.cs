using Microsoft.EntityFrameworkCore;
using ProConnect.Infrastructure.Persistence.Entity;

namespace ProConnect.Infrastructure.Persistence
{
    /// <summary>
    /// EF Core (LINQ, not raw SQL/Dapper) DbContext for Postgres/PostGIS. Created per-operation by
    /// repositories via IDbContextFactory&lt;ProConnectDbContext&gt; rather than injected directly, because
    /// the repositories themselves are registered as singletons (matching the rest of this codebase's DI
    /// style) and DbContext is not thread-safe / not meant to be shared across concurrent requests.
    /// Table/column mapping lives entirely in Persistence/Configuration/*, applied via
    /// ApplyConfigurationsFromAssembly - keeps the Domain project free of any EF Core dependency.
    /// </summary>
    public class ProConnectDbContext : DbContext
    {
        public ProConnectDbContext(DbContextOptions<ProConnectDbContext> options) : base(options)
        {
        }

        public DbSet<UserEntity> Users => Set<UserEntity>();
        public DbSet<MatchCriteriaEntity> MatchCriteria => Set<MatchCriteriaEntity>();
        public DbSet<SwipeEntity> Swipes => Set<SwipeEntity>();
        public DbSet<MatchEntity> Matches => Set<MatchEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProConnectDbContext).Assembly);
            base.OnModelCreating(modelBuilder);
        }
    }
}
