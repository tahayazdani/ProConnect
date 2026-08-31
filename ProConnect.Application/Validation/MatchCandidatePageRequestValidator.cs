using FluentValidation;
using ProConnect.Domain.Const;
using ProConnect.Domain.Request;

namespace ProConnect.Application.Validation
{
    // Same page/pagesize bounds as SearchRequestValidator, for consistency across the two paginated
    // browse endpoints in the app.
    public class MatchCandidatePageRequestValidator : AbstractValidator<MatchCandidatePageRequest>
    {
        public MatchCandidatePageRequestValidator()
        {
            RuleFor(x => x.page).GreaterThanOrEqualTo(1).WithMessage("page must be at least 1.").WithErrorCode(ErrorCode.VALIDATION.ToString());
            RuleFor(x => x.pagesize).InclusiveBetween(1, 50).WithMessage("pagesize must be between 1 and 50.").WithErrorCode(ErrorCode.VALIDATION.ToString());
        }
    }
}
