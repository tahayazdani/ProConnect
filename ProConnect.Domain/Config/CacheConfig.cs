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
        public int timeout { get; set; }
        public int databaseindex { get; set; }
        public string cachetype { get; set; }
        public ExpirationMode expirationmode { get; set; }
    }

    public class CacheAsideConfig
    {
        public bool enablecacheaside { get; set; }
        public int timeout { get; set; }
        public int cacheasidetimeout { get; set; }
    }

    public enum ExpirationMode
    {
        Default,
        None,
        Sliding,
        Absolute
    }
}
