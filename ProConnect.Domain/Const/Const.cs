using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Const
{
    public static class Const
    {
        
    }

    public static class CacheRegion
    {
        public const string PERSONPROFILE = "PersonProfile";
        public const string MATCHRECOMMENDATION = "MatchRecommendation";
        public const string SEARCHRESULT = "SearchResult";
        public const string GEOCODE = "Geocode";

        // Internal ProConnect.Domain.Request.UserRequest record (UserRepository.GetById), NOT the scraped
        // LinkedIn PersonProfileResponse - see CacheRegion.PERSONPROFILE for that. Hit on every match/search
        // request that resolves effective criteria, so it needs its own short-TTL region rather than
        // sharing PersonProfile's 7-day TTL (this data changes far more often - profile edits, deactivation).
        public const string USERPROFILE = "UserProfile";

        // A user's resolved EffectivePlan (plan + feature entitlements) - see IFeatureGateService.
        // Short-TTL, same reasoning as MATCHRECOMMENDATION: invalidated immediately on any subscription
        // change (checkout webhook, cancellation) so an upgrade/downgrade is reflected within one request,
        // not whichever is sooner of a cache rebuild or the TTL.
        public const string EFFECTIVEPLAN = "EffectivePlan";
    }

    /// <summary>
    /// Subscription plan codes - the only ones this codebase's seed data and FeatureGateService fallback
    /// logic hardcode. Everything *else* about a plan (price, display name, which features it unlocks, what
    /// their limits are) lives entirely in the subscription_plan/plan_feature tables and is editable without
    /// a deploy. Free must always exist and stay active; it's the fallback FeatureGateService resolves to
    /// for a user with no row in user_subscription at all (brand new signup) or whose subscription has
    /// lapsed (Canceled/Expired/PastDue).
    /// </summary>
    public static class PlanCode
    {
        public const string Free = "FREE";
        public const string Plus = "PLUS";
        public const string Premium = "PREMIUM";
    }

    /// <summary>
    /// String keys used as PlanFeatureEntity.feature_key / EffectivePlan.features' dictionary keys.
    /// Deliberately plain strings, not a closed enum - a brand-new feature can be gated by inserting a
    /// plan_feature row with a new key here and (optionally) adding a constant for the C# call site that
    /// checks it, without any schema change. The constants below exist only so call sites in
    /// MatchService/MessageService/etc. don't repeat magic strings - the DB doesn't restrict feature_key to
    /// this list in any way.
    /// </summary>
    public static class FeatureKey
    {
        // Counted (ResetPeriod.Daily), see MatchService.Swipe (Like path).
        public const string DailyLikes = "daily_likes";
        // Boolean flag, see MatchService.GetIncomingLikes.
        public const string WhoLikedMeVisible = "who_liked_me_visible";
        // Boolean flag, see MatchService.UpdateCriteria.
        public const string RadiusSearchEnabled = "radius_search_enabled";
        // Boolean flag - not yet wired to a call site (MessageService doesn't hide read receipts today),
        // reserved for when that's built.
        public const string ReadReceiptsEnabled = "read_receipts_enabled";
        // Boolean flag - not yet wired (no rewind/undo-pass endpoint exists today), reserved.
        public const string RewindEnabled = "rewind_enabled";
        // Counted (ResetPeriod.Monthly) grant of free Roses bundled into a plan - distinct from
        // ConsumableKey.Rose, which is the purchasable wallet balance topped up by ProductEntity purchases.
        public const string MonthlyRoses = "monthly_roses";
    }

    /// <summary>
    /// UserConsumableBalanceEntity.feature_key values - the wallet side of a la carte purchases
    /// (ProductEntity), separate from the plan-bundled FeatureKey.MonthlyRoses above.
    /// </summary>
    public static class ConsumableKey
    {
        public const string Rose = "rose";
        public const string Boost = "boost";
    }

}
