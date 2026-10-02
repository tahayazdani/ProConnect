using FluentValidation;
using ProConnect.Domain.Const;
using ProConnect.Domain.Request;

namespace ProConnect.Application.Validation
{
    // Plain text only, v1 - see PROJECT_NOTES.md Phase 5 decision. 2000 chars is generous for a chat
    // message while still bounding row/index size.
    public class MessageRequestValidator : AbstractValidator<MessageRequest>
    {
        public MessageRequestValidator()
        {
            RuleFor(x => x.content).NotEmpty().WithMessage("content is required.").WithErrorCode(ErrorCode.VALIDATION.ToString());
            RuleFor(x => x.content).MaximumLength(2000).WithMessage("content must be at most 2000 characters.").WithErrorCode(ErrorCode.VALIDATION.ToString());
        }
    }
}
