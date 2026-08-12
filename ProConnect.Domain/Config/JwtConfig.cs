using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Config
{
    public class JwtConfig
    {
        public JwtConfig()
        {

        }
        public string secret { get; set; }
        public string issuer { get; set; }
        public string audience { get; set; }
        public int expiryinminutes { get; set; }
    }
}
