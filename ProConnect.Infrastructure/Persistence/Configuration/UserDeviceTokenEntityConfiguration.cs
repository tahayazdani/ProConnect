using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProConnect.Infrastructure.Persistence.Entity;

namespace ProConnect.Infrastructure.Persistence.Configuration
{
    public class UserDeviceTokenEntityConfiguration : IEntityTypeConfiguration<UserDeviceTokenEntity>
    {
        public void Configure(EntityTypeBuilder<UserDeviceTokenEntity> builder)
        {
            builder.ToTable("user_device_token");
            builder.HasKey(t => t.userdevicetokenid);

            builder.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(t => t.userid)
                .OnDelete(DeleteBehavior.Cascade);

            // A given device_token belongs to exactly one userid at a time - see UserDeviceTokenEntity
            // remarks on why a shared-device re-registration moves the row instead of duplicating it.
            builder.HasIndex(t => t.device_token).IsUnique();

            // GetDeviceTokens(userid) - gathering every token to push to for a given user.
            builder.HasIndex(t => t.userid);
        }
    }
}
