using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using ProConnect.Infrastructure.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Repository
{
    public class UserRepository : IUserRepository
    {
        public async Task<SignUpResponse> SignUp(UserRequest request)
        {
            throw new NotImplementedException();
        }

        public async Task<UserRequest> GetById(Guid userid)
        {
            throw new NotImplementedException();
        }

        public async Task<UserRequest> GetBySub(string sub)
        {
            throw new NotImplementedException();
        }

        public async Task<UserRequest> Update(UserRequest request)
        {
            throw new NotImplementedException();
        }
    }
}
