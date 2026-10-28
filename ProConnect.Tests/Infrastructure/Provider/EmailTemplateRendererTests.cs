using Microsoft.Extensions.Logging;
using Moq;
using ProConnect.Domain.Const;
using ProConnect.Infrastructure.Provider;

namespace ProConnect.Tests.Infrastructure.Provider
{
    /// <summary>
    /// Covers EmailTemplateRenderer.Render against the one embedded template that currently exists
    /// (Welcome.html) plus the no-template branch for every other NotificationType. Doesn't assert on the
    /// internal ConcurrentDictionary cache directly (it's a private implementation detail) - the
    /// "called twice" test just checks the public output is stable/correct across calls, which is the
    /// observable behavior the cache is meant to preserve.
    /// </summary>
    public class EmailTemplateRendererTests
    {
        private static EmailTemplateRenderer CreateRenderer() =>
            new(new Mock<ILogger<EmailTemplateRenderer>>().Object);

        [Fact]
        public void Render_WelcomeWithFirstNamePlaceholder_SubstitutesTokenInSubjectAndBody()
        {
            var renderer = CreateRenderer();
            var placeholders = new Dictionary<string, string> { ["FirstName"] = "Taha" };

            var result = renderer.Render(NotificationType.Welcome, placeholders);

            Assert.NotNull(result);
            Assert.DoesNotContain("{{FirstName}}", result!.Subject);
            Assert.DoesNotContain("{{FirstName}}", result.HtmlBody);
            Assert.Contains("Taha", result.Subject);
            Assert.Contains("Taha", result.HtmlBody);
        }

        [Fact]
        public void Render_WelcomeWithEmptyPlaceholders_LeavesTokenUnsubstituted()
        {
            var renderer = CreateRenderer();
            var placeholders = new Dictionary<string, string>();

            var result = renderer.Render(NotificationType.Welcome, placeholders);

            Assert.NotNull(result);
            Assert.Contains("{{FirstName}}", result!.Subject);
            Assert.Contains("{{FirstName}}", result.HtmlBody);
        }

        [Fact]
        public void Render_WelcomeWithPlaceholdersMissingFirstNameKey_LeavesTokenUnsubstituted()
        {
            var renderer = CreateRenderer();
            var placeholders = new Dictionary<string, string> { ["SomeOtherKey"] = "value" };

            var result = renderer.Render(NotificationType.Welcome, placeholders);

            Assert.NotNull(result);
            Assert.Contains("{{FirstName}}", result!.Subject);
            Assert.Contains("{{FirstName}}", result.HtmlBody);
        }

        [Fact]
        public void Render_WithTypeThatHasNoTemplateFile_ReturnsNull()
        {
            var renderer = CreateRenderer();
            var placeholders = new Dictionary<string, string> { ["FirstName"] = "Taha" };

            var result = renderer.Render(NotificationType.NewMatch, placeholders);

            Assert.Null(result);
        }

        [Fact]
        public void Render_CalledTwiceForWelcome_ReturnsStableConsistentOutput()
        {
            var renderer = CreateRenderer();
            var placeholders = new Dictionary<string, string> { ["FirstName"] = "Taha" };

            var first = renderer.Render(NotificationType.Welcome, placeholders);
            var second = renderer.Render(NotificationType.Welcome, placeholders);

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal(first!.Subject, second!.Subject);
            Assert.Equal(first.HtmlBody, second.HtmlBody);
        }
    }
}
