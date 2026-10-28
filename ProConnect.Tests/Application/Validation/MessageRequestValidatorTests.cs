using ProConnect.Application.Validation;
using ProConnect.Domain.Request;

namespace ProConnect.Tests.Application.Validation
{
    public class MessageRequestValidatorTests
    {
        private readonly MessageRequestValidator _validator = new();

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Validate_WithEmptyContent_Fails(string? content)
        {
            var result = _validator.Validate(new MessageRequest { content = content! });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(MessageRequest.content));
        }

        [Fact]
        public void Validate_WithContentOverMaxLength_Fails()
        {
            var content = new string('a', 2001);

            var result = _validator.Validate(new MessageRequest { content = content });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(MessageRequest.content));
        }

        [Fact]
        public void Validate_WithContentAtMaxLength_Passes()
        {
            var content = new string('a', 2000);

            var result = _validator.Validate(new MessageRequest { content = content });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_WithNormalContent_Passes()
        {
            var result = _validator.Validate(new MessageRequest { content = "Hey, how's it going?" });

            Assert.True(result.IsValid);
        }
    }
}
