using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProConnect.Infrastructure.Persistence.Entity;

namespace ProConnect.Infrastructure.Persistence.Configuration
{
    public class UserConsumableBalanceEntityConfiguration : IEntityTypeConfiguration<UserConsumableBalanceEntity>
    {
        public void Configure(EntityTypeBuilder<UserConsumableBalanceEntity> builder)
        {
            builder.ToTable("user_consumable_balance");
            builder.HasKey(b => new { b.userid, b.feature_key });

            builder.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(b => b.userid)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
