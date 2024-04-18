using ProConnect.Domain.Request;
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
        public async Task SignUp(SignUpRequest request)
        {
            throw new NotImplementedException();
        }
    }
}
