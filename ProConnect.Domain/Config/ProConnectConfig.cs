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

    }
}
