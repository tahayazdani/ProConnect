using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Response
{
    public class TokenResponse
    {
        public TokenResponse()
        {
            
        }
        public string access_token { get; set; }
        public Int64 expires_in { get; set; }
        public string scope { get; set; }
        public string token_type { get; set; }
        public string id_token { get; set; }
    }
}
