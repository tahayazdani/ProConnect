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
    }
}
