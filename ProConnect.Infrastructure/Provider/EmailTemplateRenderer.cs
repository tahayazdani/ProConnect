using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using ProConnect.Domain.Const;
using ProConnect.Infrastructure.Interface;

namespace ProConnect.Infrastructure.Provider
{
    /// <summary>
    /// File-based IEmailTemplateRenderer - templates are embedded resources under
    /// ProConnect.Infrastructure/EmailTemplates/{NotificationType}.html (see the .csproj's
    /// &lt;EmbeddedResource Include="EmailTemplates\*.html" /&gt;), so they ship inside the assembly rather
    /// than needing a separate deploy step or a DB table. Deliberately NOT a DB-backed/admin-editable CMS
    /// (unlike NotificationChannelConfigEntity's on/off flag) - template copy is reviewed/versioned like
    /// code via a PR, same as every other piece of this API's user-facing text, rather than editable at
    /// runtime by whoever has DB access.
    ///
    /// File format (plain text, UTF-8):
    ///   Subject: {{FirstName}}, welcome to ProConnect!
    ///   &lt;blank line&gt;
    ///   &lt;rest of the file is the HTML body&gt;
    /// "{{Key}}" tokens anywhere in either the subject or body line are substituted from the placeholders
    /// dictionary passed to Render; a token with no matching key is left as-is rather than throwing, since a
    /// template typo should degrade to visibly-wrong copy, not take down the best-effort email send path.
    /// </summary>
    public class EmailTemplateRenderer : IEmailTemplateRenderer
    {
        private const string SubjectPrefix = "Subject:";

        // Resource name per the default MSBuild embedded-resource naming convention:
        // <AssemblyName>.<FolderPath with \ replaced by .>.<FileName>
        private static readonly string ResourceNamespace = typeof(EmailTemplateRenderer).Assembly.GetName().Name + ".EmailTemplates.";

        private readonly ILogger<EmailTemplateRenderer> _logger;

        // Templates are embedded (immutable for the process lifetime) and parsing is cheap, but there's no
        // reason to re-read/re-split the manifest resource stream on every single notification - cached
        // once per NotificationType, same "load once, reuse forever" shape as FcmPushNotificationGateway's
        // static FirebaseApp. A null cached value means "no template file exists for this type", resolved
        // once rather than re-probing the assembly manifest on every Render call for a type that will never
        // have one (e.g. in-app-only types like NewLike).
        private static readonly ConcurrentDictionary<NotificationType, (string Subject, string Body)?> _cache = new ConcurrentDictionary<NotificationType, (string Subject, string Body)?>();

        public EmailTemplateRenderer(ILogger<EmailTemplateRenderer> logger)
        {
            _logger = logger;
        }

        public RenderedEmailTemplate Render(NotificationType type, IDictionary<string, string> placeholders)
        {
            var template = _cache.GetOrAdd(type, LoadTemplate);
            if (template is null) return null;

            var subject = Substitute(template.Value.Subject, placeholders);
            var body = Substitute(template.Value.Body, placeholders);
            return new RenderedEmailTemplate(subject, body);
        }

        private (string Subject, string Body)? LoadTemplate(NotificationType type)
        {
            var resourceName = ResourceNamespace + type + ".html";
            var assembly = typeof(EmailTemplateRenderer).Assembly;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                // Not an error - see IEmailTemplateRenderer.Render remarks. Most NotificationTypes (match/
                // subscription events) are in-app/push-only today and were never meant to have an email
                // template at all.
                return null;
            }

            using var reader = new StreamReader(stream);
            var firstLine = reader.ReadLine() ?? string.Empty;
            if (!firstLine.StartsWith(SubjectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError($"Email template '{resourceName}' is missing its leading 'Subject:' line - treating it as absent.");
                return null;
            }

            var subject = firstLine.Substring(SubjectPrefix.Length).Trim();

            // The blank line separating subject from body is conventional, not required - ReadToEnd just
            // takes whatever follows the subject line as-is, so a template author who forgets the blank
            // line still gets a working (if visually ugly in the source file) template rather than a parse
            // error.
            var body = reader.ReadToEnd()?.TrimStart('\r', '\n') ?? string.Empty;

            return (subject, body);
        }

        private static string Substitute(string text, IDictionary<string, string> placeholders)
        {
            if (string.IsNullOrEmpty(text) || placeholders is null || placeholders.Count == 0)
                return text;

            foreach (var kvp in placeholders)
                text = text.Replace("{{" + kvp.Key + "}}", kvp.Value ?? string.Empty);

            return text;
        }
    }
}
