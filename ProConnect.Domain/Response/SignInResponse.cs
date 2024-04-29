using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Response
{
    public class SignInResponse
    {
        public SignInResponse()
        {
            
        }
        public string sub { get; set; }
        public bool email_verified { get; set; }
        public string name { get; set; }
        public string given_name { get; set; }
        public string family_name { get; set; }
        public string email { get; set; }
        public string picture { get; set; }
        public locale locale { get; set; }
    }

    public class locale
    {
        public locale()
        {
            
        }
        public string country { get; set; }
        public string language { get; set; }
    }

    public class Content
    {
        public Content()
        {
                
        }

        public string htmlcontent { get; set; }
    }
}
