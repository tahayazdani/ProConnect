using ProConnect.Application.Validation;
using ProConnect.Domain.Request;

namespace ProConnect.Tests.Application.Validation
{
    public class RefreshTokenRequestValidatorTests
    {
        private readonly RefreshTokenRequestValidator _validator = new();

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Validate_WithMissingToken_Fails(string? refreshtoken)
        {
            var result = _validator.Validate(new RefreshTokenRequest { refreshtoken = refreshtoken! });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(RefreshTokenRequest.refreshtoken));
        }

        [Fact]
        public void Validate_WithToken_Passes()
        {
            var result = _validator.Validate(new RefreshTokenRequest { refreshtoken = "some-raw-refresh-token" });

            Assert.True(result.IsValid);
        }
    }
}
