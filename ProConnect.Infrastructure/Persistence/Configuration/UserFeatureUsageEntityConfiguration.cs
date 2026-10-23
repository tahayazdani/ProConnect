using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProConnect.Infrastructure.Persistence.Entity;

namespace ProConnect.Infrastructure.Persistence.Configuration
{
    public class UserFeatureUsageEntityConfiguration : IEntityTypeConfiguration<UserFeatureUsageEntity>
    {
        public void Configure(EntityTypeBuilder<UserFeatureUsageEntity> builder)
        {
            builder.ToTable("user_feature_usage");
            builder.HasKey(u => new { u.userid, u.feature_key, u.period_key });

            builder.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(u => u.userid)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
