using ProConnect.Domain.Request;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Interface
{
    public interface IUserRepository
    {
        Task SignUp(SignUpRequest request);
    }
}
