using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProConnect.Infrastructure.Persistence.Entity;

namespace ProConnect.Infrastructure.Persistence.Configuration
{
    public class RefreshTokenEntityConfiguration : IEntityTypeConfiguration<RefreshTokenEntity>
    {
        public void Configure(EntityTypeBuilder<RefreshTokenEntity> builder)
        {
            builder.ToTable("refresh_token");
            builder.HasKey(t => t.refreshtokenid);

            builder.Property(t => t.tokenhash).IsRequired();

            builder.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(t => t.userid)
                .OnDelete(DeleteBehavior.Cascade);

            // RefreshTokenRepository.GetActiveByHash looks up by hash alone (the raw token is all the
            // client presents to /refreshtoken) - unique since a hash collision between two live tokens
            // would otherwise let one request redeem a different user's row.
            builder.HasIndex(t => t.tokenhash).IsUnique();

            // RefreshTokenRepository.RevokeAllForUser (Logout / DeactivateAccount / DeleteAccount) scans
            // by userid to revoke every outstanding token for that account in one pass.
            builder.HasIndex(t => t.userid);
        }
    }
}
