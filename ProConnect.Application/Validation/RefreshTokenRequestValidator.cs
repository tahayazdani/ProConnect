using FluentValidation;
using ProConnect.Application.ErrorHandler;
using ProConnect.Domain.Const;
using ProConnect.Domain.Request;

namespace ProConnect.Application.Validation
{
    public class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
    {
        public RefreshTokenRequestValidator()
        {
            RuleFor(x => x.refreshtoken).NotEmpty().WithMessage("refreshtoken is required.").WithErrorCode(ErrorCode.VALIDATION.ToString());
        }
    }
}
