using FluentValidation;
using ProConnect.Domain.Const;
using ProConnect.Domain.Request;

namespace ProConnect.Application.Validation
{
    public class SearchRequestValidator : AbstractValidator<SearchRequest>
    {
        public SearchRequestValidator()
        {
            RuleFor(x => x.page).GreaterThanOrEqualTo(1).WithMessage("page must be at least 1.").WithErrorCode(ErrorCode.VALIDATION.ToString());
            RuleFor(x => x.pagesize).InclusiveBetween(1, 50).WithMessage("pagesize must be between 1 and 50.").WithErrorCode(ErrorCode.VALIDATION.ToString());

            RuleFor(x => x.radius_km)
                .GreaterThan(0)
                .WithMessage("radius_km must be greater than 0.")
                .WithErrorCode(ErrorCode.VALIDATION.ToString())
                .When(x => x.radius_km.HasValue);

            // radius_km needs a center point to search around - without at least one of city/state/country
            // there's nothing for IGeocodingProvider to resolve into coordinates.
            RuleFor(x => x)
                .Must(x => !string.IsNullOrWhiteSpace(x.city) || !string.IsNullOrWhiteSpace(x.state) || !string.IsNullOrWhiteSpace(x.country))
                .WithMessage("radius_km requires at least one of city, state, or country to establish a search center.")
                .WithErrorCode(ErrorCode.VALIDATION.ToString())
                .When(x => x.radius_km.HasValue);
        }
    }
}
