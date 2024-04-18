using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Request
{
    public class SignUpRequest
    {
        public SignUpRequest()
        {

        }
        public string emailaddress { get; set; }
        public string username { get; set; }
    }
}
