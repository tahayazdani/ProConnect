using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Config
{
    public class CacheConfig
    {
        public CacheConfig()
        {
            cachetype = "REDIS";
        }

        public string connectionstring { get; set; }
        public int databaseindex { get; set; }
        public string cachetype { get; set; }
        public ExpirationMode expirationmode { get; set; }
    }

    public class CacheAsideConfig
    {
        public bool enablecacheaside { get; set; }
    }

    /// <summary>
    /// Per-region cache TTLs (seconds), passed as the `expiry` override to Cache.Get/Set - one
    /// CacheAsideConfig default used to apply to every region, which meant match recommendations
    /// (should be near-real-time, since swipes change them) were caching just as long as LinkedIn
    /// profile data (which barely changes). Previously hardcoded as CacheExpiry.* TimeSpan constants in
    /// ProConnect.Domain/Const/Const.cs; moved here so operators can tune staleness windows (e.g. how
    /// long a stale LinkedIn profile pic/data is served from cache) without a code change. Add one
    /// property here (plus a default in DI.GetCacheExpiryConfiguration and a line in appsettings.json)
    /// per CacheRegion as new regions get used.
    /// </summary>
    public class CacheExpiryConfig
    {
        // 7 days by default - how long a cached LinkedIn profile scrape (incl. profile pic URL) is served
        // before the next FetchLinkedInProfile call re-scrapes via IProfileProvider.
        public int personprofile { get; set; }

        // 15 min by default. Search has no invalidation trigger (unlike this), kept short purely on a
        // time basis so results don't go stale - see searchresult below.
        public int matchrecommendation { get; set; }

        // 5 min by default - MatchRecommendation is cleared on UpdateCriteria, but SearchResult has no
        // invalidation trigger at all, so it's kept short purely on a time basis so results don't go stale.
        public int searchresult { get; set; }

        // 90 days by default - a given city/state/country combination's coordinates are effectively
        // immutable, unlike per-user profile data, so this is cached indefinitely (in practice) to stay
        // well clear of Nominatim's ~1 req/sec usage policy on repeat lookups of common locations.
        public int geocode { get; set; }

        // 10 min by default - short relative to personprofile (7 days) since this mirrors our own mutable
        // UserEntity row (profile edits, deactivation) rather than a rarely-changing LinkedIn scrape - long
        // enough to absorb the hot-path GetById calls from MatchService.ResolveEffectiveCriteria on every
        // swipe/recommendation/search request, short enough that a profile edit or deactivation is
        // reflected soon even though UserRepository also actively invalidates this on every write.
        public int userprofile { get; set; }

        // 10 min by default - short-TTL like userprofile/matchrecommendation, since a resolved
        // EffectivePlan must reflect a checkout/cancellation quickly. FeatureGateService also actively
        // invalidates this region on any subscription write, same pattern as UserRepository does for
        // userprofile.
        public int effectiveplan { get; set; }
    }

    public enum ExpirationMode
    {
        Default,
        None,
        Sliding,
        Absolute
    }
}
