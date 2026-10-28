using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Interface
{
    /// <summary>
    /// Seam between the email side of INotificationService and whichever SMTP endpoint is actually
    /// configured - SmtpEmailGateway today (any SMTP relay: a real mailbox for dev, or a transactional-email
    /// provider's SMTP endpoint in prod). Same seam pattern as IPushNotificationGateway/IPaymentGateway -
    /// swapping providers, or moving to a provider's native HTTP API later, is an implementation swap behind
    /// this interface, not a rewrite of NotificationService.
    ///
    /// Best-effort only, same contract as IPushNotificationGateway - an email failure (bad address, SMTP
    /// outage, no credentials configured) must never fail the request that already durably persisted the
    /// Notification row; callers are expected to swallow/log exceptions from this rather than let them
    /// propagate.
    /// </summary>
    public interface IEmailGateway
    {
        /// <summary>Sends one HTML email to one recipient. `to` with no `@` or a null/empty value is the
        /// caller's responsibility to filter out first (e.g. a user with no emailaddress on file) - this
        /// method does not re-validate it.</summary>
        Task SendEmailAsync(string to, string subject, string htmlBody);
    }
}
