using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Config
{
    /// <summary>
    /// Two fixed-window limiter tiers, both partitioned per client IP (see Program.cs's AddRateLimiter
    /// wiring): "Global" applies to every request as a basic abuse backstop, "Strict" is opted into via
    /// [EnableRateLimiting("strict")] on endpoints that are either expensive (LinkedIn scraper - costs
    /// real money per call, see HarvestApiConfig) or auth-sensitive (signup, LinkedIn OAuth entry
    /// points) where brute-forcing/credential-stuffing or cost abuse is the actual risk.
    ///
    /// All of this is in-app, in-process-pipeline throttling. When this API sits behind an edge gateway
    /// that already enforces its own rate limits in front of this app - e.g. Azure API Management's
    /// built-in rate-limit/rate-limit-by-key/quota policies - that gateway's limiter runs before a
    /// request ever reaches this app, making this app-level limiter redundant (and a second, independent
    /// set of thresholds to keep in sync). Set <see cref="enabled"/> to false in that case rather than
    /// deleting the feature, so it can be flipped back on instantly for an environment/deployment that
    /// doesn't sit behind such a gateway (e.g. local dev, or direct App Service access).
    /// </summary>
    public class RateLimitingConfig
    {
        public RateLimitingConfig()
        {
            enabled = true;
            globalpermitlimit = 100;
            globalwindowseconds = 60;
            strictpermitlimit = 5;
            strictwindowseconds = 60;
        }

        /// <summary>
        /// Master on/off switch for this app's own rate limiting (Redis-backed fixed-window limiter +
        /// the [EnableRateLimiting("strict")] policy). Defaults to true (today's behavior) so existing
        /// deployments are unaffected unless explicitly configured otherwise. Set to false when an
        /// upstream gateway (Azure API Management, etc.) already enforces rate limits for this API, so
        /// requests aren't throttled twice under two independently-tuned sets of thresholds, and this
        /// app doesn't open an extra Redis connection purely for its own limiter.
        /// </summary>
        public bool enabled { get; set; }
        public int globalpermitlimit { get; set; }
        public int globalwindowseconds { get; set; }
        public int strictpermitlimit { get; set; }
        public int strictwindowseconds { get; set; }
    }
}
