using ProConnect.Application.Validation;
using ProConnect.Domain.Request;

namespace ProConnect.Tests.Application.Validation
{
    public class MatchCandidatePageRequestValidatorTests
    {
        private readonly MatchCandidatePageRequestValidator _validator = new();

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Validate_WithPageBelowOne_Fails(int page)
        {
            var result = _validator.Validate(new MatchCandidatePageRequest { page = page, pagesize = 10 });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(MatchCandidatePageRequest.page));
        }

        [Fact]
        public void Validate_WithPageOne_Passes()
        {
            var result = _validator.Validate(new MatchCandidatePageRequest { page = 1, pagesize = 10 });

            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(51)]
        public void Validate_WithPageSizeOutOfRange_Fails(int pagesize)
        {
            var result = _validator.Validate(new MatchCandidatePageRequest { page = 1, pagesize = pagesize });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(MatchCandidatePageRequest.pagesize));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(50)]
        public void Validate_WithPageSizeAtBounds_Passes(int pagesize)
        {
            var result = _validator.Validate(new MatchCandidatePageRequest { page = 1, pagesize = pagesize });

            Assert.True(result.IsValid);
        }
    }
}
