using System.Collections.Generic;
using ProConnect.Domain.Const;

namespace ProConnect.Domain.Response
{
    /// <summary>
    /// Client-facing plan catalog row - see ISubscriptionService.GetPlans. Maps 1:1 off Model.SubscriptionPlan
    /// plus its PlanFeature rows, flattened into PlanFeatureResponse so a pricing-page UI can render the full
    /// feature matrix (which plan has what, at what limit) in a single call.
    /// </summary>
    public class SubscriptionPlanResponse
    {
        public SubscriptionPlanResponse()
        {

        }
        public string plan_code { get; set; }
        public string display_name { get; set; }
        public string description { get; set; }
        public decimal price_monthly { get; set; }
        public decimal price_annual { get; set; }
        public int sort_order { get; set; }
        public List<PlanFeatureResponse> features { get; set; } = new();
    }

    public class PlanFeatureResponse
    {
        public PlanFeatureResponse()
        {

        }
        public string feature_key { get; set; }
        public bool is_enabled { get; set; }
        public int? limit_value { get; set; }
        public ResetPeriod reset_period { get; set; } = ResetPeriod.None;
    }
}
