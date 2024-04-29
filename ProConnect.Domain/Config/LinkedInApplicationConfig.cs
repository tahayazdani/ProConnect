using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Config
{
    public class LinkedInApplicationConfig
    {
        public LinkedInApplicationConfig()
        {
            
        }
        public string client_id { get; set; }
        public string client_secret { get; set;}
        public string redirect_url { get; set; }
        public string linkedin_baseurl { get; set; }
    }
}
