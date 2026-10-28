using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Config
{
    /// <summary>
    /// Previously the CORS policy in Program.cs allowed any origin at all
    /// (SetIsOriginAllowed(origin => true)) combined with AllowCredentials() - which lets any website on
    /// the internet make credentialed requests against this API. This config makes the allowed origin
    /// list explicit and environment-specific instead (e.g. the real client app's origin(s) in
    /// production, localhost dev ports in appsettings.Development.json).
    /// </summary>
    public class CorsConfig
    {
        public CorsConfig()
        {
            allowedorigins = Array.Empty<string>();
        }

        public string[] allowedorigins { get; set; }
    }
}
