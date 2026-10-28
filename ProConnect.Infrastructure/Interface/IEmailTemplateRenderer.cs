using System.Collections.Generic;
using ProConnect.Domain.Const;

namespace ProConnect.Infrastructure.Interface
{
    /// <summary>Result of a successful IEmailTemplateRenderer.Render call - just the two things
    /// IEmailGateway.SendEmailAsync needs.</summary>
    public sealed class RenderedEmailTemplate
    {
        public RenderedEmailTemplate(string subject, string htmlBody)
        {
            Subject = subject;
            HtmlBody = htmlBody;
        }

        public string Subject { get; }
        public string HtmlBody { get; }
    }

    /// <summary>
    /// Resolves a NotificationType to its campaign/notification email template (if one exists) and fills in
    /// placeholders - the "define the template somewhere" piece: one template per NotificationType, not per
    /// call site, so MatchService/SubscriptionService/UserService never embed email copy inline the way
    /// Notify's title/body strings do today for the in-app channel.
    /// </summary>
    public interface IEmailTemplateRenderer
    {
        /// <summary>
        /// Returns the rendered subject/HTML body for `type` with every "{{Key}}" token in `placeholders`
        /// substituted, or null if no template file exists for `type` yet. A null return is deliberately not
        /// an error - NotificationChannelConfigEntity can have Email enabled for a type before its template
        /// is written (e.g. mid-rollout), and that must be a silent no-op, mirroring "no device tokens
        /// registered" being a no-op for the push channel rather than a failure.
        /// </summary>
        RenderedEmailTemplate Render(NotificationType type, IDictionary<string, string> placeholders);
    }
}
