using FluentValidation;
using ProConnect.Domain.Const;
using ProConnect.Domain.Request;

namespace ProConnect.Application.Validation
{
    // CRITERIA-04 fix: every field on MatchCriteriaRequest is optional (nullable), so none of these
    // rules fire for a field the caller leaves unset - only validates the ones actually supplied.
    // Before this validator existed, min_age/max_age had no bounds at all: min_age > max_age (an
    // impossible range that would silently return zero matches forever) and negative ages were both
    // accepted without complaint.
    public class MatchCriteriaRequestValidator : AbstractValidator<MatchCriteriaRequest>
    {
        public MatchCriteriaRequestValidator()
        {
            RuleFor(x => x.min_age)
                .GreaterThanOrEqualTo(18)
                .WithMessage("min_age must be at least 18.")
                .WithErrorCode(ErrorCode.VALIDATION.ToString())
                .When(x => x.min_age.HasValue);

            RuleFor(x => x.max_age)
                .LessThanOrEqualTo(120)
                .WithMessage("max_age must be at most 120.")
                .WithErrorCode(ErrorCode.VALIDATION.ToString())
                .When(x => x.max_age.HasValue);

            RuleFor(x => x)
                .Must(x => x.min_age.Value <= x.max_age.Value)
                .WithMessage("min_age must be less than or equal to max_age.")
                .WithErrorCode(ErrorCode.VALIDATION.ToString())
                .When(x => x.min_age.HasValue && x.max_age.HasValue);

            RuleFor(x => x.radius_km)
                .GreaterThan(0)
                .WithMessage("radius_km must be greater than 0.")
                .WithErrorCode(ErrorCode.VALIDATION.ToString())
                .When(x => x.radius_km.HasValue);
        }
    }
}
