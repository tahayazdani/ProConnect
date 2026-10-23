using System;
using System.Collections.Generic;

namespace ProConnect.Domain.Model
{
    /// <summary>
    /// One row from subscription_plan - the catalog of plans a user can subscribe to (Const.PlanCode.*
    /// for the hardcoded codes Free/Plus/Premium seed as, but this table can hold more). Price/name/active
    /// flag all live here so they're editable without a deploy - see PROJECT_NOTES.md Phase 7. Carries its
    /// PlanFeatureEntity rows so SubscriptionService.GetPlans can render a full feature matrix in one call
    /// without a second round trip per plan.
    /// </summary>
    public class SubscriptionPlan
    {
        public SubscriptionPlan()
        {

        }
        public Guid planid { get; set; }

        /// <summary>Stable code, e.g. Const.PlanCode.Free/Plus/Premium - what UserSubscription.plan_code
        /// and FeatureGateService's Free fallback reference. Not the display name.</summary>
        public string plan_code { get; set; }
        public string display_name { get; set; }
        public string description { get; set; }

        /// <summary>USD, billed per BillingInterval. 0 for the Free plan.</summary>
        public decimal price_monthly { get; set; }
        public decimal price_annual { get; set; }

        /// <summary>Stripe Price IDs backing each interval - what StripePaymentGateway.CreateCheckoutSession
        /// passes as the line item. Null for Free (never actually checked out through Stripe).</summary>
        public string stripe_price_id_monthly { get; set; }
        public string stripe_price_id_annual { get; set; }

        /// <summary>Soft-disables a plan from new checkouts (e.g. retiring a tier) without deleting it or
        /// breaking existing subscribers still on it - see SubscriptionRepository.GetActivePlans.</summary>
        public bool is_active { get; set; } = true;

        /// <summary>Display/catalog ordering only (Free, then Plus, then Premium) - not a priority or
        /// ranking used anywhere in FeatureGateService's resolution logic.</summary>
        public int sort_order { get; set; }

        public List<PlanFeature> features { get; set; } = new();
    }
}
