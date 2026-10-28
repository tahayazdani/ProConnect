namespace ProConnect.Domain.Config
{
    /// <summary>
    /// Controls app.UseForwardedHeaders(...) in Program.cs - required for httpContext.Connection.RemoteIpAddress
    /// (and therefore rate-limiting's IP fallback, Serilog request logging, and HTTPS-redirect scheme
    /// correctness) to reflect the real client rather than Azure's own front-end/reverse-proxy node. On
    /// Azure App Service the public internet never connects to the container directly - only Azure's
    /// front end does - and it's that front end which appends the real client IP to X-Forwarded-For.
    /// </summary>
    public class ForwardedHeadersConfig
    {
        public ForwardedHeadersConfig()
        {
            // 1 trusted hop = App Service's own front end only. Bump this if Azure Front Door, App
            // Gateway, or APIM is added in front of App Service later - each adds exactly one more hop
            // that itself appends (rather than passes through verbatim) to X-Forwarded-For. Set this too
            // low and the IP resolved is one of Azure's own infra IPs rather than the client's - a safe
            // (merely imprecise) failure. Set it too high and a client-forged leftmost X-Forwarded-For
            // entry could be trusted as the "real" client IP - a spoofing risk. See
            // ForwardedHeadersMiddleware's ForwardLimit semantics for why the count must be exact, not
            // just "big enough".
            trustedproxyhopcount = 1;
        }

        public int trustedproxyhopcount { get; set; }
    }
}
