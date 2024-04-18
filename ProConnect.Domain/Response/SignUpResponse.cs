using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Response
{
    public class SignUpResponse
    {
        public SignUpResponse()
        {

        }
        public Guid userid { get; set; }
        public string username { get; set; }
    }
}
