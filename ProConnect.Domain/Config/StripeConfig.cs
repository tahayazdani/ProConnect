namespace ProConnect.Domain.Config
{
    /// <summary>
    /// Config for StripePaymentGateway (Infrastructure/Provider) - the whole point of the IPaymentGateway
    /// seam is that swapping providers, or just rotating keys, is "plug in by API key" - edit these values
    /// (via user-secrets locally, Azure Key Vault/App Config in other environments), no code change. See
    /// PROJECT_NOTES.md Phase 7 decision.
    /// </summary>
    public class StripeConfig
    {
        public StripeConfig()
        {

        }
        /// <summary>Secret API key (sk_test_... / sk_live_...) - StripeClient's credential. Set via
        /// `dotnet user-secrets set "ProConnectConfig:StripeConfig:secretkey" "<value>"` in ProConnect.WebApi;
        /// do not commit a real value here.</summary>
        public string secretkey { get; set; }

        /// <summary>Publishable key (pk_test_... / pk_live_...) - safe to expose to a client if needed, but
        /// kept here too so one config section fully describes the Stripe integration.</summary>
        public string publishablekey { get; set; }

        /// <summary>Signing secret (whsec_...) for the /webhook endpoint - StripePaymentGateway.
        /// ParseWebhookEvent verifies every inbound event's signature against this before trusting it.</summary>
        public string webhooksecret { get; set; }
    }
}
