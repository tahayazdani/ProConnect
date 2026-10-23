using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProConnect.Domain.Const;
using ProConnect.Infrastructure.Persistence.Entity;

namespace ProConnect.Infrastructure.Persistence.Configuration
{
    public class PlanFeatureEntityConfiguration : IEntityTypeConfiguration<PlanFeatureEntity>
    {
        public void Configure(EntityTypeBuilder<PlanFeatureEntity> builder)
        {
            builder.ToTable("plan_feature");
            builder.HasKey(f => f.planfeatureid);

            builder.HasOne<SubscriptionPlanEntity>()
                .WithMany()
                .HasForeignKey(f => f.planid)
                .OnDelete(DeleteBehavior.Cascade);

            // One row per (plan, feature) - FeatureGateService.ResolveEffectivePlan reads this whole set
            // for a user's plan in one query.
            builder.HasIndex(f => new { f.planid, f.feature_key }).IsUnique();

            // Default seed matrix for Free/Plus/Premium (Const.FeatureKey.*) - this is the "move a feature
            // between tiers by flipping a flag" table: everything below is freely editable afterward
            // (is_enabled/limit_value/reset_period, or adding a brand-new feature_key row) with no deploy.
            // A null limit_value on an enabled counted feature means "enabled, no cap" (Premium's likes).
            var free = SubscriptionPlanEntityConfiguration.FreePlanId;
            var plus = SubscriptionPlanEntityConfiguration.PlusPlanId;
            var premium = SubscriptionPlanEntityConfiguration.PremiumPlanId;

            builder.HasData(
                // Free
                new PlanFeatureEntity { planfeatureid = Guid.Parse("11111111-1111-0000-0000-000000000001"), planid = free, feature_key = FeatureKey.DailyLikes, is_enabled = true, limit_value = 10, reset_period = ResetPeriod.Daily },
                new PlanFeatureEntity { planfeatureid = Guid.Parse("11111111-1111-0000-0000-000000000002"), planid = free, feature_key = FeatureKey.WhoLikedMeVisible, is_enabled = false, reset_period = ResetPeriod.None },
                new PlanFeatureEntity { planfeatureid = Guid.Parse("11111111-1111-0000-0000-000000000003"), planid = free, feature_key = FeatureKey.RadiusSearchEnabled, is_enabled = false, reset_period = ResetPeriod.None },
                new PlanFeatureEntity { planfeatureid = Guid.Parse("11111111-1111-0000-0000-000000000004"), planid = free, feature_key = FeatureKey.ReadReceiptsEnabled, is_enabled = false, reset_period = ResetPeriod.None },
                new PlanFeatureEntity { planfeatureid = Guid.Parse("11111111-1111-0000-0000-000000000005"), planid = free, feature_key = FeatureKey.RewindEnabled, is_enabled = false, reset_period = ResetPeriod.None },
                new PlanFeatureEntity { planfeatureid = Guid.Parse("11111111-1111-0000-0000-000000000006"), planid = free, feature_key = FeatureKey.MonthlyRoses, is_enabled = false, limit_value = 0, reset_period = ResetPeriod.Monthly },

                // Plus
                new PlanFeatureEntity { planfeatureid = Guid.Parse("22222222-2222-0000-0000-000000000001"), planid = plus, feature_key = FeatureKey.DailyLikes, is_enabled = true, limit_value = 50, reset_period = ResetPeriod.Daily },
                new PlanFeatureEntity { planfeatureid = Guid.Parse("22222222-2222-0000-0000-000000000002"), planid = plus, feature_key = FeatureKey.WhoLikedMeVisible, is_enabled = true, reset_period = ResetPeriod.None },
                new PlanFeatureEntity { planfeatureid = Guid.Parse("22222222-2222-0000-0000-000000000003"), planid = plus, feature_key = FeatureKey.RadiusSearchEnabled, is_enabled = true, reset_period = ResetPeriod.None },
                new PlanFeatureEntity { planfeatureid = Guid.Parse("22222222-2222-0000-0000-000000000004"), planid = plus, feature_key = FeatureKey.ReadReceiptsEnabled, is_enabled = false, reset_period = ResetPeriod.None },
                new PlanFeatureEntity { planfeatureid = Guid.Parse("22222222-2222-0000-0000-000000000005"), planid = plus, feature_key = FeatureKey.RewindEnabled, is_enabled = true, reset_period = ResetPeriod.None },
                new PlanFeatureEntity { planfeatureid = Guid.Parse("22222222-2222-0000-0000-000000000006"), planid = plus, feature_key = FeatureKey.MonthlyRoses, is_enabled = true, limit_value = 1, reset_period = ResetPeriod.Monthly },

                // Premium
                new PlanFeatureEntity { planfeatureid = Guid.Parse("33333333-3333-0000-0000-000000000001"), planid = premium, feature_key = FeatureKey.DailyLikes, is_enabled = true, limit_value = null, reset_period = ResetPeriod.Daily },
                new PlanFeatureEntity { planfeatureid = Guid.Parse("33333333-3333-0000-0000-000000000002"), planid = premium, feature_key = FeatureKey.WhoLikedMeVisible, is_enabled = true, reset_period = ResetPeriod.None },
                new PlanFeatureEntity { planfeatureid = Guid.Parse("33333333-3333-0000-0000-000000000003"), planid = premium, feature_key = FeatureKey.RadiusSearchEnabled, is_enabled = true, reset_period = ResetPeriod.None },
                new PlanFeatureEntity { planfeatureid = Guid.Parse("33333333-3333-0000-0000-000000000004"), planid = premium, feature_key = FeatureKey.ReadReceiptsEnabled, is_enabled = true, reset_period = ResetPeriod.None },
                new PlanFeatureEntity { planfeatureid = Guid.Parse("33333333-3333-0000-0000-000000000005"), planid = premium, feature_key = FeatureKey.RewindEnabled, is_enabled = true, reset_period = ResetPeriod.None },
                new PlanFeatureEntity { planfeatureid = Guid.Parse("33333333-3333-0000-0000-000000000006"), planid = premium, feature_key = FeatureKey.MonthlyRoses, is_enabled = true, limit_value = 5, reset_period = ResetPeriod.Monthly }
            );
        }
    }
}
