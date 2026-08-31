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
    }

    /// <summary>
    /// Per-region cache TTLs, passed as the `expiry` override to Cache.Get/Set - one CacheAsideConfig
    /// default used to apply to every region, which meant match recommendations (should be near-real-time,
    /// since swipes change them) were caching just as long as LinkedIn profile data (which barely changes).
    /// Add one constant here per CacheRegion as new regions get used.
    /// </summary>
    public static class CacheExpiry
    {
        public static readonly TimeSpan PersonProfile = TimeSpan.FromDays(7);
        public static readonly TimeSpan MatchRecommendation = TimeSpan.FromMinutes(15);

        // Search has no invalidation trigger (unlike MatchRecommendation, which is cleared on
        // UpdateCriteria) - kept short purely on a time basis so results don't go stale.
        public static readonly TimeSpan SearchResult = TimeSpan.FromMinutes(5);

        // A given city/state/country combination's coordinates are effectively immutable, unlike
        // per-user profile data - cached indefinitely (in practice) to stay well clear of Nominatim's
        // ~1 req/sec usage policy on repeat lookups of common locations.
        public static readonly TimeSpan Geocode = TimeSpan.FromDays(90);

        // Short relative to PersonProfile (7 days) since this mirrors our own mutable UserEntity row
        // (profile edits, deactivation) rather than a rarely-changing LinkedIn scrape - long enough to
        // absorb the hot-path GetById calls from MatchService.ResolveEffectiveCriteria on every
        // swipe/recommendation/search request, short enough that a profile edit or deactivation is
        // reflected soon even though UserRepository also actively invalidates this on every write.
        public static readonly TimeSpan UserProfile = TimeSpan.FromMinutes(10);
    }
}
