namespace ProConnect.Domain.Config
{
    /// <summary>
    /// Config for SmtpEmailGateway (Infrastructure/Provider) - confines every SMTP-specific detail (host,
    /// port, credentials, TLS) behind one section, same "built but dark until real credentials exist" story
    /// as PushConfig/StripeConfig. Works against any SMTP endpoint (a real mailbox for dev/testing, or a
    /// transactional-email provider's SMTP relay - SendGrid/Mailgun/SES/Postmark all expose one) without a
    /// code change; swapping to a provider's native HTTP API later is an IEmailGateway implementation swap,
    /// not a rewrite of anything that calls IEmailGateway today.
    /// </summary>
    public class EmailConfig
    {
        public EmailConfig()
        {

        }

        /// <summary>SMTP host. Set via `dotnet user-secrets set "ProConnectConfig:EmailConfig:host" "<value>"`
        /// in ProConnect.WebApi; do not commit a real value here.</summary>
        public string host { get; set; }

        /// <summary>SMTP port - 587 (STARTTLS) is the common default for most providers.</summary>
        public int port { get; set; } = 587;

        /// <summary>Set via `dotnet user-secrets set "ProConnectConfig:EmailConfig:username" "<value>"` -
        /// do not commit a real value here.</summary>
        public string username { get; set; }

        /// <summary>Set via `dotnet user-secrets set "ProConnectConfig:EmailConfig:password" "<value>"` -
        /// do not commit a real value here.</summary>
        public string password { get; set; }

        /// <summary>Whether to negotiate TLS (STARTTLS/implicit SSL depending on port) - true for every
        /// mainstream provider; only false against a local dev SMTP catcher (e.g. MailHog/smtp4dev).</summary>
        public bool usessl { get; set; } = true;

        /// <summary>"From" address campaign/notification emails are sent as - not actually secret, but kept
        /// alongside the rest of the email config the same way StripeConfig keeps publishablekey alongside
        /// its secret keys.</summary>
        public string fromaddress { get; set; }

        /// <summary>"From" display name, e.g. "ProConnect".</summary>
        public string fromname { get; set; }

        public int timeoutinseconds { get; set; } = 30;
    }
}
