using ProConnect.Domain.Response;
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
        public string public_identifier { get; set; }
        public string gender { get; set; }
        public date birth_date { get; set; }
    }
}
