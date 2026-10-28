using ProConnect.Application.Validation;
using ProConnect.Domain.Request;

namespace ProConnect.Tests.Application.Validation
{
    public class SearchRequestValidatorTests
    {
        private readonly SearchRequestValidator _validator = new();

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Validate_WithPageBelowOne_Fails(int page)
        {
            var result = _validator.Validate(new SearchRequest { page = page, pagesize = 10 });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchRequest.page));
        }

        [Fact]
        public void Validate_WithPageOne_Passes()
        {
            var result = _validator.Validate(new SearchRequest { page = 1, pagesize = 10 });

            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(51)]
        public void Validate_WithPageSizeOutOfRange_Fails(int pagesize)
        {
            var result = _validator.Validate(new SearchRequest { page = 1, pagesize = pagesize });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchRequest.pagesize));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(50)]
        public void Validate_WithPageSizeAtBounds_Passes(int pagesize)
        {
            var result = _validator.Validate(new SearchRequest { page = 1, pagesize = pagesize });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_WithRadiusZero_Fails()
        {
            var result = _validator.Validate(new SearchRequest { page = 1, pagesize = 10, radius_km = 0, city = "Austin" });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchRequest.radius_km));
        }

        [Fact]
        public void Validate_WithRadiusSetButNoCityStateOrCountry_Fails()
        {
            var result = _validator.Validate(new SearchRequest { page = 1, pagesize = 10, radius_km = 10 });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == "radius_km requires at least one of city, state, or country to establish a search center.");
        }

        [Fact]
        public void Validate_WithRadiusAndCity_Passes()
        {
            var result = _validator.Validate(new SearchRequest { page = 1, pagesize = 10, radius_km = 10, city = "Austin" });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_WithRadiusAndState_Passes()
        {
            var result = _validator.Validate(new SearchRequest { page = 1, pagesize = 10, radius_km = 10, state = "TX" });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_WithRadiusAndCountry_Passes()
        {
            var result = _validator.Validate(new SearchRequest { page = 1, pagesize = 10, radius_km = 10, country = "USA" });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_WithoutRadius_DoesNotRequireLocation()
        {
            var result = _validator.Validate(new SearchRequest { page = 1, pagesize = 10 });

            Assert.True(result.IsValid);
        }
    }
}
