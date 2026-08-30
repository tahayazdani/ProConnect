using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Config
{
    public class GeocodingConfig
    {
        public GeocodingConfig()
        {

        }

        public string baseurl { get; set; }
        // Nominatim's usage policy requires a descriptive User-Agent identifying the application/contact.
        public string useragent { get; set; }
        public int timeoutinseconds { get; set; }
    }
}
