using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProConnect.Infrastructure.Persistence.Entity;

namespace ProConnect.Infrastructure.Persistence.Configuration
{
    public class SubscriptionPlanEntityConfiguration : IEntityTypeConfiguration<SubscriptionPlanEntity>
    {
        // Fixed GUIDs (not Guid.NewGuid()) because HasData is snapshotted into the migration at build time -
        // a random value here would churn on every `dotnet ef migrations add`. Referenced by
        // PlanFeatureEntityConfiguration's seed rows below via these same constants.
        public static readonly Guid FreePlanId = Guid.Parse("11111111-0000-0000-0000-000000000001");
        public static readonly Guid PlusPlanId = Guid.Parse("22222222-0000-0000-0000-000000000002");
        public static readonly Guid PremiumPlanId = Guid.Parse("33333333-0000-0000-0000-000000000003");

        public void Configure(EntityTypeBuilder<SubscriptionPlanEntity> builder)
        {
            builder.ToTable("subscription_plan");
            builder.HasKey(p => p.planid);
            builder.HasIndex(p => p.plan_code).IsUnique();

            builder.Property(p => p.price_monthly).HasColumnType("numeric(10,2)");
            builder.Property(p => p.price_annual).HasColumnType("numeric(10,2)");

            // Default seed: Free/Plus/Premium (Const.PlanCode.*) - everything here (price, display_name,
            // is_active) is editable afterward straight in the table, no deploy required. Free is seeded
            // active and must stay that way - see PlanCode remarks on why FeatureGateService depends on it.
            builder.HasData(
                new SubscriptionPlanEntity
                {
                    planid = FreePlanId,
                    plan_code = Domain.Const.PlanCode.Free,
                    display_name = "Free",
                    description = "Get started for free.",
                    price_monthly = 0m,
                    price_annual = 0m,
                    is_active = true,
                    sort_order = 1
                },
                new SubscriptionPlanEntity
                {
                    planid = PlusPlanId,
                    plan_code = Domain.Const.PlanCode.Plus,
                    display_name = "Plus",
                    description = "More likes, see who liked you, and search by radius.",
                    price_monthly = 9.99m,
                    price_annual = 59.99m,
                    is_active = true,
                    sort_order = 2
                },
                new SubscriptionPlanEntity
                {
                    planid = PremiumPlanId,
                    plan_code = Domain.Const.PlanCode.Premium,
                    display_name = "Premium",
                    description = "Unlimited likes and every feature, unlocked.",
                    price_monthly = 19.99m,
                    price_annual = 119.99m,
                    is_active = true,
                    sort_order = 3
                }
            );
        }
    }
}
