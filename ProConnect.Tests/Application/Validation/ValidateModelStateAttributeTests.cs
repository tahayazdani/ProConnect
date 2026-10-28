using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json;
using ProConnect.Application.Validation;
using ProConnect.Domain.Response;
using System.Collections.Generic;

namespace ProConnect.Tests.Application.Validation
{
    /// <summary>
    /// ValidateModelStateAttribute.OnActionExecuting has two branches. When a FluentValidation
    /// ValidationResult was previously stashed on HttpContext.Items under "ValidationResult" (see
    /// ValidatorInterceptorTests for how it gets there), the branch taken is driven by
    /// context.ModelState.IsValid, not by the stashed result's own IsValid - a failed business
    /// validation response (500/VALIDATION) is only returned once ASP.NET's own ModelState also
    /// disagrees. Otherwise (no stash, or a non-ValidationResult stashed item), a still-invalid
    /// ModelState means a raw model-binding failure (malformed route GUID, bad enum, etc.) slipped
    /// through - see PROJECT_NOTES.md/BUGS_FOUND.md G-6 - and gets a clean 400 built straight from
    /// ModelState instead of silently letting the action run with a default-bound value.
    /// </summary>
    public class ValidateModelStateAttributeTests
    {
        private static ActionExecutingContext CreateContext(object? validationResultItem)
        {
            var httpContext = new DefaultHttpContext();
            if (validationResultItem != null)
            {
                httpContext.Items["ValidationResult"] = validationResultItem;
            }

            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: new object());
        }

        [Fact]
        public void OnActionExecuting_WithNoStashedResultAndValidModelState_DoesNotSetResult()
        {
            var attribute = new ValidateModelStateAttribute();
            var context = CreateContext(validationResultItem: null);

            attribute.OnActionExecuting(context);

            Assert.Null(context.Result);
        }

        [Fact]
        public void OnActionExecuting_WithNoStashedValidationResultButInvalidModelState_SetsBadRequestResult()
        {
            var attribute = new ValidateModelStateAttribute();
            var context = CreateContext(validationResultItem: null);
            context.ModelState.AddModelError("targetUserId", "The value 'not-a-guid' is not valid.");

            attribute.OnActionExecuting(context);

            var result = Assert.IsType<OkObjectResult>(context.Result);
            Assert.Equal(400, result.StatusCode);

            var body = JsonConvert.DeserializeObject<ProConnectResponse<string>>((string)result.Value!);
            Assert.NotNull(body);
            Assert.False(body!.issuccessfull);
            Assert.Equal((int)ProConnect.Domain.Const.ErrorCode.VALIDATION, body.statuscode);
            var error = Assert.Single(body.errors);
            Assert.Equal("The value 'not-a-guid' is not valid.", error.errormessage);
            Assert.Equal("ModelBinding", error.errorcode);
        }

        [Fact]
        public void OnActionExecuting_WithNonValidationResultStashedItemAndInvalidModelState_SetsBadRequestResult()
        {
            var attribute = new ValidateModelStateAttribute();
            var context = CreateContext(validationResultItem: "not-a-validation-result");
            context.ModelState.AddModelError("field", "bad value");

            attribute.OnActionExecuting(context);

            var result = Assert.IsType<OkObjectResult>(context.Result);
            Assert.Equal(400, result.StatusCode);
        }

        [Fact]
        public void OnActionExecuting_WithValidModelStateDespiteStashedFailure_DoesNotSetResult()
        {
            var attribute = new ValidateModelStateAttribute();
            var failedResult = new ValidationResult(new[] { new ValidationFailure("field", "bad value") });
            var context = CreateContext(failedResult);
            // context.ModelState is untouched here, so it's still valid - the attribute checks ModelState,
            // not the stashed FluentValidation result's own IsValid.

            attribute.OnActionExecuting(context);

            Assert.Null(context.Result);
        }

        [Fact]
        public void OnActionExecuting_WithInvalidModelStateAndStashedFailure_SetsValidationErrorResult()
        {
            var attribute = new ValidateModelStateAttribute();
            var failure = new ValidationFailure("email", "Email is required") { ErrorCode = "NotEmpty" };
            var failedResult = new ValidationResult(new[] { failure });
            var context = CreateContext(failedResult);
            context.ModelState.AddModelError("email", "Email is required");

            attribute.OnActionExecuting(context);

            var result = Assert.IsType<OkObjectResult>(context.Result);
            Assert.Equal(500, result.StatusCode);

            var body = JsonConvert.DeserializeObject<ProConnectResponse<string>>((string)result.Value!);
            Assert.NotNull(body);
            Assert.False(body!.issuccessfull);
            Assert.Equal((int)ProConnect.Domain.Const.ErrorCode.VALIDATION, body.statuscode);
            Assert.Equal("Validation", body.errormessage);
            var error = Assert.Single(body.errors);
            Assert.Equal("Email is required", error.errormessage);
            Assert.Equal("NotEmpty", error.errorcode);
        }
    }
}
