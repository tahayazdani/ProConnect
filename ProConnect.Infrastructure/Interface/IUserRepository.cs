using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Interface
{
    public interface IUserRepository
    {
        Task<SignUpResponse> SignUp(UserRequest request);
        Task<UserRequest> GetById(Guid userid);
        Task<UserRequest> GetBySub(string sub);
        Task<UserRequest> Update(UserRequest request);
    }
}
