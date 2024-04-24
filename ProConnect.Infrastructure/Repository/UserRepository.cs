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
        public async Task<SignUpResponse> SignUp(PersonProfileResponse request)
        {
            throw new NotImplementedException();
        }
    }
}
