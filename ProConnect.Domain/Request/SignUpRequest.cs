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
        public string email { get; set; }
        public string sub { get; set; }
        public bool email_verified { get; set; }
        public string name { get; set; }
        public string given_name { get; set; }
        public string family_name { get; set; }
        public string picture { get; set; }
        public locale locale { get; set; }

        public string gender { get; set; }
        public date birth_date { get; set; }
        public string public_identifier { get; set; }
    }
    public class locale
    {
        public locale()
        {

        }
        public string country { get; set; }
        public string language { get; set; }
    }
}
