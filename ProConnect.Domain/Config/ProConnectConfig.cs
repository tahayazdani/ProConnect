using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Config
{
    public class ProConnectConfig
    {
        public ProConnectConfig()
        {

        }
        public HarvestApiConfig harvestApiConfig { get; set; }
        public LinkedInApplicationConfig linkedInApplicationConfig { get; set; }
        public JwtConfig jwtConfig { get; set; }
        public DataBaseConfig dataBaseConfig { get; set; }
        public GeocodingConfig geocodingConfig { get; set; }
        public StripeConfig stripeConfig { get; set; }
        public PushConfig pushConfig { get; set; }
        public EmailConfig emailConfig { get; set; }
        public CorsConfig corsConfig { get; set; }
        public RateLimitingConfig rateLimitingConfig { get; set; }
        public ForwardedHeadersConfig forwardedHeadersConfig { get; set; }

    }
}
