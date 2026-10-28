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
            if (context.HttpContext.Items.TryGetValue("ValidationResult", out var value) && value is ValidationResult vldResult)
            {
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
                return;
            }

            // No FluentValidation result was ever stashed for this request (that only happens for a
            // validator-bound request body), but ASP.NET's own model binding may still have rejected it
            // outright - a malformed route/query GUID, an unparsable enum value, etc. Program.cs sets
            // SuppressModelStateInvalidFilter = true, which disables ApiController's automatic 400 for
            // exactly this case, so without this branch the action would run anyway using whatever
            // default the binder fell back to (e.g. Guid.Empty) instead of being rejected. See
            // PROJECT_NOTES.md / BUGS_FOUND.md G-6.
            if (!context.ModelState.IsValid)
            {
                var bindingErrors = context.ModelState
                    .Where(kvp => kvp.Value != null && kvp.Value.Errors.Count > 0)
                    .SelectMany(kvp => kvp.Value!.Errors.Select(e => new Errors
                    {
                        errormessage = string.IsNullOrWhiteSpace(e.ErrorMessage)
                            ? $"The value provided for '{kvp.Key}' is invalid."
                            : e.ErrorMessage,
                        errorcode = "ModelBinding"
                    }))
                    .ToList();

                var responseObj = JsonConvert.SerializeObject(new ProConnectResponse<string>
                {
                    body = string.Empty,
                    statuscode = (int)ErrorCode.VALIDATION,
                    errormessage = "Validation",
                    message = "One or more request values could not be parsed.",
                    issuccessfull = false,
                    errors = bindingErrors
                }).ToString();

                context.Result = new OkObjectResult(responseObj)
                {
                    StatusCode = 400
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
