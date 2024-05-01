using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Config
{
    public class ProxyCurlConfig
    {
        public ProxyCurlConfig() { }

        public string authorizationtoken { get; set; }
        public string baseurl { get; set; }
        public string extra { get; set; }
        public string live_scrape { get; set; }
        public string skills { get; set; }
        public string use_cache { get; set; }
        public string fallback_to_cache { get; set; }
        public string personal_email { get; set; }
    }
}
