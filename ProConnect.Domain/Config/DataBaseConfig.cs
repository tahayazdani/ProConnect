using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Config
{
    public class DataBaseConfig
    {
        public DataBaseConfig()
        {

        }

        public string connectionstring { get; set; }

        // Explicit Npgsql pool sizing - applied on top of `connectionstring` in Program.cs rather than
        // left to Npgsql's built-in defaults (Min Pool Size=0, Max Pool Size=100), so pool size is a
        // config knob per-environment (e.g. bump maxpoolsize per API instance as instance count/QPS
        // grows) instead of a silent default nobody's looked at. Null means "use the fallback baked into
        // Program.cs".
        public int? minpoolsize { get; set; }
        public int? maxpoolsize { get; set; }

        // Recycles idle pooled connections after this many seconds so a traffic spike's connection count
        // doesn't linger indefinitely once load drops back down. Null means "use Npgsql's default (300s)".
        public int? connectionidlelifetimeseconds { get; set; }
    }
}
