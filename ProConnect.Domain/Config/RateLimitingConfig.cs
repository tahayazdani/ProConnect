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
    /// </summary>
    public class RateLimitingConfig
    {
        public RateLimitingConfig()
        {
            globalpermitlimit = 100;
            globalwindowseconds = 60;
            strictpermitlimit = 5;
            strictwindowseconds = 60;
        }

        public int globalpermitlimit { get; set; }
        public int globalwindowseconds { get; set; }
        public int strictpermitlimit { get; set; }
        public int strictwindowseconds { get; set; }
    }
}
