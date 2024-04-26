using ProConnect.Domain.Const;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Response
{
    public class ProConnectResponse<T> where T : class
    {
        public ProConnectResponse() { }
        public T body { get; set; }
        public string message { get; set; }
        public int statuscode { get; set; } = 200;
        public string errormessage { get; set; }
        public List<Errors> errors { get; set; }
        public bool issuccessfull { get; set; } = true;
    }

    public class Errors
    {
        public string errormessage { get; set; }
        public string errorcode { get; set; }
    }
}
