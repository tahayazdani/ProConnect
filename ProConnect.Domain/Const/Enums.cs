using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Const
{
    public class Enums
    {

    }

    public enum ErrorCode
    {
        OK = 200,

        USERISEXIST = 300,
        USERISNOTEXIST = 301,

        VALIDATION = 450,
        INVALIDEMAILADDRESS = 451,
        NOTADULT = 452,
        INVALIDDATEFORMAT = 453,

        SYSTEM = 500
    }

    public enum SwipeAction
    {
        Like,
        Pass
    }
}
