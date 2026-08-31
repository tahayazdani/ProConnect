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
        public DbSet<BlockEntity> Blocks => Set<BlockEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Backs the GIN trigram indexes in UserEntityConfiguration (full_name/headline/occupation/
            // company/industry/city/state/country) - without pg_trgm, a leading-wildcard ILIKE
            // ('%term%', as SearchRepository.Search uses) can't use a plain B-tree index at all and falls
            // back to a full table scan. postgis is registered implicitly by npgsqlOptions.UseNetTopologySuite()
            // (see Program.cs), but pg_trgm has no such auto-registration and needs this explicit call.
            modelBuilder.HasPostgresExtension("pg_trgm");

            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProConnectDbContext).Assembly);
            base.OnModelCreating(modelBuilder);
        }
    }
}
