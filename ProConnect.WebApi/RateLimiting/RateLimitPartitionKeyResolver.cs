namespace ProConnect.WebApi.RateLimiting
{
    /// <summary>
    /// Single place that decides what "one caller" means for rate-limiting purposes - the userid claim
    /// (set by TokenService.GenerateToken on every access token) when the request is authenticated,
    /// else the client IP. IP is only the fallback (not the primary key) because a single NAT'd IP
    /// (office network, mobile carrier CGNAT) can front many distinct signed-in users, who would
    /// otherwise unfairly share one bucket once authenticated.
    ///
    /// httpContext.Connection.RemoteIpAddress is trustworthy here specifically because Program.cs calls
    /// app.UseForwardedHeaders(...) before app.UseRateLimiter() - that middleware rewrites
    /// RemoteIpAddress itself from the trusted X-Forwarded-For hop(s), so this resolver never needs to
    /// parse a header directly or know anything about Azure's reverse-proxy topology.
    /// </summary>
    internal static class RateLimitPartitionKeyResolver
    {
        // Matches the custom claim type TokenService.GenerateToken issues - see
        // "new Claim("userid", userid.ToString())" there. Deliberately not ClaimTypes.NameIdentifier
        // (also set on the same token) just to keep this resolver pointed at the one claim this
        // codebase already treats as the canonical user id elsewhere (see BaseController's
        // HttpContext.User?.FindFirst("userid") usage).
        private const string UserIdClaimType = "userid";

        public static string Resolve(HttpContext httpContext)
        {
            var userId = httpContext.User?.FindFirst(UserIdClaimType)?.Value;
            if (!string.IsNullOrWhiteSpace(userId))
            {
                return $"user:{userId}";
            }

            var ip = httpContext.Connection.RemoteIpAddress?.ToString();
            return !string.IsNullOrWhiteSpace(ip) ? $"ip:{ip}" : "ip:unknown";
        }
    }
}
