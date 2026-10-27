using FluentValidation;
using ProConnect.Domain.Const;
using ProConnect.Domain.Request;

namespace ProConnect.Application.Validation
{
    // BUG-22 fix: NotificationPageRequest had no validator registered at all (not even a manual guard in
    // NotificationService), so page<=0 flowed straight into EF's Skip() as a negative offset, which
    // Postgres itself rejects with "2201X: OFFSET must not be negative" - an unhandled exception that
    // surfaced as a raw-leaking 500 (reproduced in BUGS_FOUND.md BUG-22 / G-4). Also caps pagesize so
    // pagesize=0 can no longer produce the has_more-always-true pagination deadlock noted for the sibling
    // TransactionPageRequest (G-27) - same bounds as MatchCandidatePageRequestValidator/
    // MessagePageRequestValidator for consistency across the app's paginated endpoints.
    public class NotificationPageRequestValidator : AbstractValidator<NotificationPageRequest>
    {
        public NotificationPageRequestValidator()
        {
            RuleFor(x => x.page).GreaterThanOrEqualTo(1).WithMessage("page must be at least 1.").WithErrorCode(ErrorCode.VALIDATION.ToString());
            RuleFor(x => x.pagesize).InclusiveBetween(1, 50).WithMessage("pagesize must be between 1 and 50.").WithErrorCode(ErrorCode.VALIDATION.ToString());
        }
    }
}
