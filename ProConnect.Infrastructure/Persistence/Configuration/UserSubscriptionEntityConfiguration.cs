using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProConnect.Infrastructure.Persistence.Entity;

namespace ProConnect.Infrastructure.Persistence.Configuration
{
    public class UserSubscriptionEntityConfiguration : IEntityTypeConfiguration<UserSubscriptionEntity>
    {
        public void Configure(EntityTypeBuilder<UserSubscriptionEntity> builder)
        {
            builder.ToTable("user_subscription");
            builder.HasKey(s => s.usersubscriptionid);

            // At most one subscription row per user - same one-row-per-user shape as MatchCriteriaEntity,
            // enforced here instead of using userid as the PK directly so usersubscriptionid stays a
            // stable identifier across a (hypothetical future) plan-change history without a key change.
            builder.HasIndex(s => s.userid).IsUnique();

            builder.HasOne<UserEntity>()
                .WithOne()
                .HasForeignKey<UserSubscriptionEntity>(s => s.userid)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne<SubscriptionPlanEntity>()
                .WithMany()
                .HasForeignKey(s => s.planid)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(s => s.stripe_customer_id);
            builder.HasIndex(s => s.stripe_subscription_id);
        }
    }
}
