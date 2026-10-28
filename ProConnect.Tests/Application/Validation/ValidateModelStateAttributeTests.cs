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
    /// ValidateModelStateAttribute.OnActionExecuting only acts when a FluentValidation ValidationResult was
    /// previously stashed on HttpContext.Items under "ValidationResult" (see ValidatorInterceptorTests for
    /// how it gets there) - and even then, the actual branch taken is driven by context.ModelState.IsValid,
    /// not by the stashed result's own IsValid. That's the specific behavior these tests pin down.
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
        public void OnActionExecuting_WithNoStashedValidationResult_DoesNotSetResult()
        {
            var attribute = new ValidateModelStateAttribute();
            var context = CreateContext(validationResultItem: null);
            context.ModelState.AddModelError("field", "bad value");

            attribute.OnActionExecuting(context);

            Assert.Null(context.Result);
        }

        [Fact]
        public void OnActionExecuting_WithNonValidationResultStashedItem_DoesNotSetResult()
        {
            var attribute = new ValidateModelStateAttribute();
            var context = CreateContext(validationResultItem: "not-a-validation-result");
            context.ModelState.AddModelError("field", "bad value");

            attribute.OnActionExecuting(context);

            Assert.Null(context.Result);
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
