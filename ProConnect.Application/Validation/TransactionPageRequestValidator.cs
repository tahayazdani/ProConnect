using FluentValidation;
using ProConnect.Domain.Const;
using ProConnect.Domain.Request;

namespace ProConnect.Application.Validation
{
    // BUG-22 fix: same gap as NotificationPageRequestValidator (see its remarks) - TransactionPageRequest
    // had no validator registered either, so page<=0 produced the identical leaked
    // "2201X: OFFSET must not be negative" 500 (BUGS_FOUND.md BUG-22 / G-4b), and pagesize=0 produced the
    // has_more-always-true pagination deadlock separately flagged as G-27. Same bounds as the other
    // paginated-request validators for consistency.
    public class TransactionPageRequestValidator : AbstractValidator<TransactionPageRequest>
    {
        public TransactionPageRequestValidator()
        {
            RuleFor(x => x.page).GreaterThanOrEqualTo(1).WithMessage("page must be at least 1.").WithErrorCode(ErrorCode.VALIDATION.ToString());
            RuleFor(x => x.pagesize).InclusiveBetween(1, 50).WithMessage("pagesize must be between 1 and 50.").WithErrorCode(ErrorCode.VALIDATION.ToString());
        }
    }
}
