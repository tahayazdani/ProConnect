using System;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using ProConnect.Domain.Config;
using ProConnect.Infrastructure.Interface;

namespace ProConnect.Infrastructure.Provider
{
    /// <summary>
    /// IEmailGateway implementation backed by plain SMTP (MailKit) - see IEmailGateway for the seam this
    /// sits behind. Every MailKit/MimeKit type is confined to this class, same "one adapter owns the whole
    /// vendor SDK" rule FcmPushNotificationGateway follows for FirebaseAdmin and StripePaymentGateway
    /// follows for Stripe.net. Works unmodified against a real mailbox (dev/testing) or any transactional-
    /// email provider's SMTP relay (SendGrid/Mailgun/SES/Postmark all expose one) - only EmailConfig changes.
    /// </summary>
    public class SmtpEmailGateway : IEmailGateway
    {
        private readonly EmailConfig _config;
        private readonly ILogger<SmtpEmailGateway> _logger;

        public SmtpEmailGateway(IOptions<ProConnectConfig> proConnectConfig, ILogger<SmtpEmailGateway> logger)
        {
            _config = proConnectConfig.Value?.emailConfig
                ?? throw new InvalidOperationException("ProConnectConfig:EmailConfig configuration section is missing.");
            _logger = logger;
        }

        public async Task SendEmailAsync(string to, string subject, string htmlBody)
        {
            if (string.IsNullOrWhiteSpace(to))
                throw new ArgumentException("A recipient address is required.", nameof(to));

            // Checked here (call time), not the constructor, same "built but dark until real credentials
            // exist" story as FcmPushNotificationGateway/StripePaymentGateway - a missing/blank host only
            // surfaces when an email is actually attempted, which NotificationService's best-effort wrapper
            // catches and logs, rather than the whole app failing to start without real SMTP credentials yet.
            if (string.IsNullOrWhiteSpace(_config.host))
                throw new InvalidOperationException("ProConnectConfig:EmailConfig:host is not configured.");

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(
                string.IsNullOrWhiteSpace(_config.fromname) ? _config.fromaddress : _config.fromname,
                _config.fromaddress));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

            using var client = new SmtpClient();
            client.Timeout = _config.timeoutinseconds * 1000;

            await client.ConnectAsync(
                _config.host,
                _config.port,
                _config.usessl ? SecureSocketOptions.StartTlsWhenAvailable : SecureSocketOptions.None);

            if (!string.IsNullOrWhiteSpace(_config.username))
                await client.AuthenticateAsync(_config.username, _config.password);

            try
            {
                await client.SendAsync(message);
            }
            finally
            {
                await client.DisconnectAsync(true);
            }
        }
    }
}
