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

            // Soft-delete/deactivation flag - default true at the DB level so the migration backfills
            // every pre-existing row as active, and any raw insert that forgets to set it doesn't
            // silently create a deactivated user. Indexed because every user-lookup/listing query
            // (GetById, GetBySub, match candidates, search) filters on it.
            builder.Property(u => u.is_active).HasDefaultValue(true);
            builder.HasIndex(u => u.is_active);
        }
    }
}
