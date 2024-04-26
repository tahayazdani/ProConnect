using FluentValidation;
using ProConnect.Application.ErrorHandler;
using ProConnect.Domain.Const;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Application.Validation
{
    public class SignUpValidator : AbstractValidator<SignUpRequest>
    {
        public SignUpValidator()
        {
            RuleFor(x => x.emailaddress).EmailAddress().WithMessage("Please enter valid email address.").WithErrorCode(ErrorCode.INVALIDEMAILADDRESS.ToString());
            RuleFor(x => x.birth_date).Must(ValidateBirthDate).WithMessage("Please enter valid birth date.").WithErrorCode(ErrorCode.INVALIDDATEFORMAT.ToString());
            RuleFor(x => x.birth_date).Must(IsAdult).WithMessage("User must be +18.").WithErrorCode(ErrorCode.NOTADULT.ToString());
        }

        private bool ValidateBirthDate(date birthdate)
        {
            var str = $"{birthdate.year}-{birthdate.month}-{birthdate.day}";
            return DateTime.TryParse(str, out DateTime birth);
        }

        private bool IsAdult(date birthdate)
        {
            var str = $"{birthdate.year}-{birthdate.month}-{birthdate.day}";
            var result = DateTime.TryParse(str, out DateTime birth);
            if (!result)
                return false;

            return birth.AddYears(18) < DateTime.Now;
        }
    }
}
