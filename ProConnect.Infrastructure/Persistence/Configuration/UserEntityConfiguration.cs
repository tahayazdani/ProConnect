using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProConnect.Infrastructure.Persistence.Entity;

namespace ProConnect.Infrastructure.Persistence.Configuration
{
    public class UserEntityConfiguration : IEntityTypeConfiguration<UserEntity>
    {
        public void Configure(EntityTypeBuilder<UserEntity> builder)
        {
            builder.ToTable("users");
            builder.HasKey(u => u.userid);

            // sub is the LinkedIn OIDC subject - looked up on every LinkedInAuthentication sign-in, so it
            // needs an index; unique because one LinkedIn identity should only ever map to one local account.
            builder.HasIndex(u => u.sub).IsUnique();

            // public_identifier is looked up on GetLinkedInProfile and re-signup; not guaranteed globally
            // unique in our data (harvestapi identifiers), so indexed but not unique.
            builder.HasIndex(u => u.public_identifier);

            builder.Property(u => u.location).HasColumnType("geography (Point, 4326)");
            builder.HasIndex(u => u.location).HasMethod("GIST");

            builder.Property(u => u.interests).HasColumnType("text[]");

            // Generated/computed columns (Postgres GENERATED ALWAYS AS (...) STORED) so MatchRepository can
            // push age-range and industry filtering into indexed SQL WHERE clauses instead of loading every
            // active user and filtering in memory - see MatchRepository.GetCandidates.
            builder.Property(u => u.birthdate)
                .HasColumnType("date")
                .HasComputedColumnSql("make_date(birth_year, birth_month, birth_day)", stored: true);
            builder.HasIndex(u => u.birthdate);

            builder.Property(u => u.industry_normalized)
                .HasColumnType("text")
                .HasComputedColumnSql("lower(industry)", stored: true);
            builder.HasIndex(u => u.industry_normalized);

            // make_date() throws for out-of-range values (e.g. month 13, day 31 for February) instead of
            // returning null, so birth_day/birth_month need to be constrained at the DB level - otherwise a
            // write with a bad value would fail with an opaque Postgres error from the generated column
            // instead of a clear constraint violation. NULL is always allowed (partial/unknown birth date).
            builder.ToTable(tb =>
            {
                tb.HasCheckConstraint("CK_users_birth_month_range", "birth_month IS NULL OR (birth_month BETWEEN 1 AND 12)");
                tb.HasCheckConstraint("CK_users_birth_day_range", "birth_day IS NULL OR (birth_day BETWEEN 1 AND 31)");
            });

            // Soft-delete/deactivation flag - default true at the DB level so the migration backfills
            // every pre-existing row as active, and any raw insert that forgets to set it doesn't
            // silently create a deactivated user. Indexed because every user-lookup/listing query
            // (GetById, GetBySub, match candidates, search) filters on it.
            builder.Property(u => u.is_active).HasDefaultValue(true);
            builder.HasIndex(u => u.is_active);

            // GIN trigram indexes (pg_trgm, registered in ProConnectDbContext.OnModelCreating) - every one
            // of these columns is searched with a leading-wildcard ILIKE('%term%') in
            // SearchRepository.Search, which a plain B-tree index (or none at all, as before this change)
            // can't accelerate at all; Postgres has to fall back to a full table scan of `users`. A GIN
            // trigram index lets ILIKE substring search use an index regardless of wildcard position.
            // Doesn't affect MatchRepository.GetCandidates' industry/city/state/country filters - those are
            // exact ILIKE matches (no wildcard) against industry_normalized/city/state/country and are
            // already served by the existing btree indexes (industry_normalized) or are cheap enough
            // unindexed given they're always combined with other hard filters first.
            builder.HasIndex(u => u.full_name).HasMethod("gin").HasOperators("gin_trgm_ops");
            builder.HasIndex(u => u.headline).HasMethod("gin").HasOperators("gin_trgm_ops");
            builder.HasIndex(u => u.occupation).HasMethod("gin").HasOperators("gin_trgm_ops");
            builder.HasIndex(u => u.company).HasMethod("gin").HasOperators("gin_trgm_ops");
            builder.HasIndex(u => u.industry).HasMethod("gin").HasOperators("gin_trgm_ops");
            builder.HasIndex(u => u.city).HasMethod("gin").HasOperators("gin_trgm_ops");
            builder.HasIndex(u => u.state).HasMethod("gin").HasOperators("gin_trgm_ops");
            builder.HasIndex(u => u.country).HasMethod("gin").HasOperators("gin_trgm_ops");
        }
    }
}
