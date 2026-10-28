using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Config
{
    public class JwtConfig
    {
        public JwtConfig()
        {

        }
        public string secret { get; set; }
        public string issuer { get; set; }
        public string audience { get; set; }
        public int expiryinminutes { get; set; }

        // How long an issued refresh token stays redeemable (see RefreshTokenEntity /
        // TokenService.GenerateRefreshToken). Deliberately much longer-lived than the access token above -
        // the access token is what's checked on every request and is kept short so a leaked one expires
        // quickly, while the refresh token only gets presented to the one dedicated /refreshtoken endpoint
        // and is rotated on every use, so a longer TTL here just means "how long before an idle user has to
        // fully sign in again" rather than widening the exposure window the way a longer access-token TTL would.
        public int refreshtokenexpiryindays { get; set; } = 30;
    }
}
