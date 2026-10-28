using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProConnect.Application.Interface;
using ProConnect.Domain.Config;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace ProConnect.Application.Service
{
    public class TokenService : ITokenService
    {
        private readonly JwtConfig _jwtConfig;

        public TokenService(IOptions<ProConnectConfig> proConnectConfig)
        {
            _jwtConfig = proConnectConfig.Value?.jwtConfig
                ?? throw new InvalidOperationException("ProConnectConfig:JwtConfig configuration section is missing.");
        }

        public string GenerateToken(Guid userid, string public_identifier, string email)
        {
            var claims = new List<Claim>
            {
                new Claim("userid", userid.ToString()),
                new Claim(ClaimTypes.NameIdentifier, userid.ToString())
            };

            if (!string.IsNullOrEmpty(public_identifier))
                claims.Add(new Claim("public_identifier", public_identifier));

            if (!string.IsNullOrEmpty(email))
                claims.Add(new Claim(ClaimTypes.Email, email));

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtConfig.secret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _jwtConfig.issuer,
                audience: _jwtConfig.audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_jwtConfig.expiryinminutes),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // 256 bits of entropy from a CSPRNG, URL-safe base64 - plenty to make guessing/brute-forcing
        // infeasible, and nothing here is derived from user input (unlike the JWT above) so there's no
        // claims payload to encode.
        public string GenerateRefreshToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        public string HashToken(string rawtoken)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawtoken));
            return Convert.ToHexString(bytes);
        }
    }
}
