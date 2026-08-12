using System;

namespace ProConnect.Application.Interface
{
    public interface ITokenService
    {
        string GenerateToken(Guid userid, string public_identifier, string email);
    }
}
