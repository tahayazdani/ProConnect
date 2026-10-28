using Microsoft.AspNetCore.Http;
using ProConnect.WebApi.RateLimiting;
using System.Net;
using System.Security.Claims;

namespace ProConnect.Tests.WebApi.RateLimiting
{
    /// <summary>
    /// RateLimitPartitionKeyResolver is internal - testable here via InternalsVisibleTo on
    /// ProConnect.WebApi.csproj. Resolve has exactly two branches: a "userid" claim (authenticated caller)
    /// takes priority over client IP; IP is only consulted when there's no claim, and "ip:unknown" is the
    /// final fallback when even RemoteIpAddress is null.
    /// </summary>
    public class RateLimitPartitionKeyResolverTests
    {
        [Fact]
        public void Resolve_WithUserIdClaim_ReturnsUserPrefixedKey()
        {
            var httpContext = new DefaultHttpContext();
            var identity = new ClaimsIdentity(new[] { new Claim("userid", "12345") });
            httpContext.User = new ClaimsPrincipal(identity);
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");

            var key = RateLimitPartitionKeyResolver.Resolve(httpContext);

            Assert.Equal("user:12345", key);
        }

        [Fact]
        public void Resolve_WithNoUserIdClaimButRemoteIp_ReturnsIpPrefixedKey()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

            var key = RateLimitPartitionKeyResolver.Resolve(httpContext);

            Assert.Equal("ip:203.0.113.7", key);
        }

        [Fact]
        public void Resolve_WithBlankUserIdClaim_FallsBackToIp()
        {
            var httpContext = new DefaultHttpContext();
            var identity = new ClaimsIdentity(new[] { new Claim("userid", "   ") });
            httpContext.User = new ClaimsPrincipal(identity);
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

            var key = RateLimitPartitionKeyResolver.Resolve(httpContext);

            Assert.Equal("ip:203.0.113.7", key);
        }

        [Fact]
        public void Resolve_WithNoClaimAndNoRemoteIp_ReturnsIpUnknown()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = null;

            var key = RateLimitPartitionKeyResolver.Resolve(httpContext);

            Assert.Equal("ip:unknown", key);
        }
    }
}
