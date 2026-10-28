using ProConnect.Application.Validation;
using ProConnect.Domain.Request;

namespace ProConnect.Tests.Application.Validation
{
    public class MatchCriteriaRequestValidatorTests
    {
        private readonly MatchCriteriaRequestValidator _validator = new();

        [Fact]
        public void Validate_WithAllFieldsUnset_Passes()
        {
            var result = _validator.Validate(new MatchCriteriaRequest());

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_WithMinAgeBelow18_Fails()
        {
            var result = _validator.Validate(new MatchCriteriaRequest { min_age = 17 });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(MatchCriteriaRequest.min_age));
        }

        [Fact]
        public void Validate_WithMinAgeAt18_Passes()
        {
            var result = _validator.Validate(new MatchCriteriaRequest { min_age = 18 });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_WithMaxAgeAbove120_Fails()
        {
            var result = _validator.Validate(new MatchCriteriaRequest { max_age = 121 });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(MatchCriteriaRequest.max_age));
        }

        [Fact]
        public void Validate_WithMaxAgeAt120_Passes()
        {
            var result = _validator.Validate(new MatchCriteriaRequest { max_age = 120 });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_WithMinAgeGreaterThanMaxAge_Fails()
        {
            var result = _validator.Validate(new MatchCriteriaRequest { min_age = 40, max_age = 30 });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == "min_age must be less than or equal to max_age.");
        }

        [Fact]
        public void Validate_WithMinAgeEqualToMaxAge_Passes()
        {
            var result = _validator.Validate(new MatchCriteriaRequest { min_age = 30, max_age = 30 });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_WithOnlyMinAgeSet_DoesNotRunCrossFieldRule()
        {
            var result = _validator.Validate(new MatchCriteriaRequest { min_age = 25 });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_WithRadiusZero_Fails()
        {
            var result = _validator.Validate(new MatchCriteriaRequest { radius_km = 0 });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(MatchCriteriaRequest.radius_km));
        }

        [Fact]
        public void Validate_WithRadiusNegative_Fails()
        {
            var result = _validator.Validate(new MatchCriteriaRequest { radius_km = -5 });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(MatchCriteriaRequest.radius_km));
        }

        [Fact]
        public void Validate_WithRadiusPositive_Passes()
        {
            var result = _validator.Validate(new MatchCriteriaRequest { radius_km = 10 });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_WithRadiusUnset_Passes()
        {
            var result = _validator.Validate(new MatchCriteriaRequest { radius_km = null });

            Assert.True(result.IsValid);
        }
    }
}
