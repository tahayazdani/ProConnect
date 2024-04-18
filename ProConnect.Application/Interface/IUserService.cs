using ProConnect.Domain.Config;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Application.Interface
{
    public interface IUserService
    {
        Task<ProConnectResponse<SignUpResponse>> SignUp(SignUpRequest request, ProxyCurlConfig proxyCurlConfig);    
    }
}
