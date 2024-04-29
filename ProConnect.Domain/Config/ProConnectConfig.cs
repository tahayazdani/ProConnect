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
        public ProxyCurlConfig proxyCurlConfig { get; set; }
        public CacheConfig redisConfig { get; set; }
        public DataBaseConfig dataBaseConfig { get; set; }
        public LinkedInApplicationConfig linkedInApplicationConfig { get; set; }

    }
}
