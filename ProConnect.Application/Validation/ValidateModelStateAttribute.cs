using FluentValidation;
using FluentValidation.AspNetCore;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json;
using ProConnect.Application.ErrorHandler;
using ProConnect.Domain.Const;
using ProConnect.Domain.Response;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValidationResult = FluentValidation.Results.ValidationResult;

namespace ProConnect.Application.Validation
{
    public class ValidateModelStateAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {

            if (!context.HttpContext.Items.TryGetValue("ValidationResult", out var value))
            {
                return;
            }
            if (!(value is ValidationResult vldResult))
            {
                return;
            }


            if (!context.ModelState.IsValid)
            {
                var responseObj = JsonConvert.SerializeObject(new ProConnectResponse<string>
                {
                    body = string.Empty,
                    statuscode = (int)ErrorCode.VALIDATION,
                    errormessage = "Validation",
                    message = $"Something went wrong, Please call administrator.",
                    issuccessfull = false,
                    errors = vldResult.Errors.Select(p => new Errors { errormessage = p.ErrorMessage, errorcode = p.ErrorCode }).ToList()
                }).ToString();

                context.Result = new OkObjectResult(responseObj)
                {
                    StatusCode = 500
                };
            }
        }
    }

    public class ValidatorInterceptor : IValidatorInterceptor
    {
        public FluentValidation.Results.ValidationResult AfterAspNetValidation(ActionContext actionContext, IValidationContext validationContext, FluentValidation.Results.ValidationResult result)
        {
            if (!result.IsValid)
            {
                actionContext.HttpContext.Items.Add("ValidationResult", result);
            }
            return result;
        }

        public IValidationContext BeforeAspNetValidation(ActionContext actionContext, IValidationContext commonContext)
        {
            return commonContext;
        }

        public ValidationContext BeforeMvcValidation(ControllerContext controllerContext, ValidationContext validationContext)
        {
            return validationContext;
        }
    }

}
