using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ProConnect.Domain.Config;
using ProConnect.Infrastructure.Provider;
using System;

namespace ProConnect.Tests.Infrastructure.Provider
{
    /// <summary>
    /// SmtpEmailGateway confines the MailKit SDK entirely to itself with no injected abstraction over it,
    /// so its actual send path (SmtpClient.ConnectAsync/SendAsync) can't be unit tested without a real/fake
    /// SMTP server. What's testable: the recipient-required argument check and the lazy "built but dark"
    /// host-configured check, both of which throw before any SmtpClient is even constructed.
    /// </summary>
    public class SmtpEmailGatewayTests
    {
        private SmtpEmailGateway CreateGateway(EmailConfig config)
        {
            var proConnectConfig = Options.Create(new ProConnectConfig { emailConfig = config });
            return new SmtpEmailGateway(proConnectConfig, new Mock<ILogger<SmtpEmailGateway>>().Object);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task SendEmailAsync_WithBlankRecipient_ThrowsArgumentException(string to)
        {
            var gateway = CreateGateway(new EmailConfig { host = "smtp.example.com" });

            await Assert.ThrowsAsync<ArgumentException>(() => gateway.SendEmailAsync(to, "subject", "<p>body</p>"));
        }

        [Fact]
        public async Task SendEmailAsync_WithNoHostConfigured_ThrowsInvalidOperationException()
        {
            var gateway = CreateGateway(new EmailConfig { host = null });

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                gateway.SendEmailAsync("someone@example.com", "subject", "<p>body</p>"));
        }

        [Fact]
        public async Task SendEmailAsync_RecipientCheckRunsBeforeHostCheck_ThrowsArgumentExceptionEvenWithoutHost()
        {
            var gateway = CreateGateway(new EmailConfig { host = null });

            await Assert.ThrowsAsync<ArgumentException>(() => gateway.SendEmailAsync("", "subject", "<p>body</p>"));
        }
    }
}
