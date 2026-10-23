using System;

namespace ProConnect.Infrastructure.Persistence.Entity
{
    /// <summary>
    /// EF Core persistence model mirroring ProConnect.Domain.Model.SubscriptionPlan (minus its features
    /// list, which PlanFeatureEntity carries separately via planid - SubscriptionRepository joins the two).
    /// Seeded with Free/Plus/Premium (see SubscriptionPlanEntityConfiguration) - everything about a plan
    /// beyond its Const.PlanCode is editable in this table without a deploy, see PROJECT_NOTES.md Phase 7.
    /// </summary>
    public class SubscriptionPlanEntity
    {
        public Guid planid { get; set; }
        public string plan_code { get; set; }
        public string display_name { get; set; }
        public string? description { get; set; }
        public decimal price_monthly { get; set; }
        public decimal price_annual { get; set; }
        public string? stripe_price_id_monthly { get; set; }
        public string? stripe_price_id_annual { get; set; }
        public bool is_active { get; set; } = true;
        public int sort_order { get; set; }
    }
}
