using ProConnect.Application.Validation;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;

namespace ProConnect.Tests.Application.Validation
{
    public class SignUpValidatorTests
    {
        private readonly SignUpValidator _validator = new();

        private static SignUpRequest ValidRequest() => new()
        {
            email = "jdoe@example.com",
            public_identifier = "jdoe",
            birth_date = new date { year = 1990, month = 1, day = 1 }
        };

        [Fact]
        public void Validate_WithValidRequest_Passes()
        {
            var result = _validator.Validate(ValidRequest());

            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData("not-an-email")]
        [InlineData("missing-at-sign.com")]
        public void Validate_WithInvalidEmail_Fails(string email)
        {
            var request = ValidRequest();
            request.email = email;

            var result = _validator.Validate(request);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(SignUpRequest.email));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Validate_WithMissingPublicIdentifier_Fails(string? publicIdentifier)
        {
            var request = ValidRequest();
            request.public_identifier = publicIdentifier!;

            var result = _validator.Validate(request);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(SignUpRequest.public_identifier));
        }

        [Fact]
        public void Validate_WithNullBirthDate_FailsWithInvalidDateFormat()
        {
            var request = ValidRequest();
            request.birth_date = null!;

            var result = _validator.Validate(request);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(SignUpRequest.birth_date) && e.ErrorMessage == "Please enter valid birth date.");
        }

        [Fact]
        public void Validate_WithUnparsableBirthDate_FailsWithInvalidDateFormat()
        {
            var request = ValidRequest();
            request.birth_date = new date { year = 2023, month = 2, day = 30 };

            var result = _validator.Validate(request);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(SignUpRequest.birth_date) && e.ErrorMessage == "Please enter valid birth date.");
        }

        [Fact]
        public void Validate_WithBirthDateUnder18_FailsWithNotAdult()
        {
            var request = ValidRequest();
            var recent = DateTime.Now.AddYears(-10);
            request.birth_date = new date { year = recent.Year, month = recent.Month, day = recent.Day };

            var result = _validator.Validate(request);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(SignUpRequest.birth_date) && e.ErrorMessage == "User must be +18.");
        }

        [Fact]
        public void Validate_WithBirthDateTurning18Tomorrow_Fails()
        {
            // IsAdult uses a strict "<" comparison (birth.AddYears(18) < DateTime.Now), so someone whose
            // 18th birthday is tomorrow (i.e. still 17 today) is rejected.
            var turns18Tomorrow = DateTime.Now.AddYears(-18).AddDays(1);
            var request = ValidRequest();
            request.birth_date = new date { year = turns18Tomorrow.Year, month = turns18Tomorrow.Month, day = turns18Tomorrow.Day };

            var result = _validator.Validate(request);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(SignUpRequest.birth_date) && e.ErrorMessage == "User must be +18.");
        }

        [Fact]
        public void Validate_WithBirthDateOver18_Passes()
        {
            var over18 = DateTime.Now.AddYears(-19);
            var request = ValidRequest();
            request.birth_date = new date { year = over18.Year, month = over18.Month, day = over18.Day };

            var result = _validator.Validate(request);

            Assert.True(result.IsValid);
        }
    }
}
