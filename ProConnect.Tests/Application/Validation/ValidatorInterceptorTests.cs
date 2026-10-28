using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Moq;
using ProConnect.Application.Validation;
using System.ComponentModel.DataAnnotations;
using FluentValidationResult = FluentValidation.Results.ValidationResult;
using FluentValidationFailure = FluentValidation.Results.ValidationFailure;

namespace ProConnect.Tests.Application.Validation
{
    /// <summary>
    /// ValidatorInterceptor.AfterAspNetValidation is the other half of the handoff ValidateModelStateAttribute
    /// depends on: it's the only place "ValidationResult" gets written onto HttpContext.Items, and only when
    /// the FluentValidation result actually failed. BeforeAspNetValidation/BeforeMvcValidation are both pure
    /// passthroughs (BeforeMvcValidation isn't even part of IValidatorInterceptor in the referenced
    /// FluentValidation.AspNetCore version - it's a dangling public method, never invoked by the pipeline -
    /// but it's still public and cheaply testable as a plain identity method).
    /// </summary>
    public class ValidatorInterceptorTests
    {
        private static ActionContext CreateActionContext(HttpContext? httpContext = null) =>
            new ActionContext(httpContext ?? new DefaultHttpContext(), new RouteData(), new ActionDescriptor());

        private static ActionContext CreateControllerActionContext(HttpContext? httpContext = null) =>
            new ActionContext(httpContext ?? new DefaultHttpContext(), new RouteData(), new ControllerActionDescriptor());

        [Fact]
        public void AfterAspNetValidation_WithFailedResult_StashesResultOnHttpContextItems()
        {
            var interceptor = new ValidatorInterceptor();
            var httpContext = new DefaultHttpContext();
            var actionContext = CreateActionContext(httpContext);
            var failedResult = new FluentValidationResult(new[] { new FluentValidationFailure("field", "bad value") });

            var returned = interceptor.AfterAspNetValidation(actionContext, Mock.Of<IValidationContext>(), failedResult);

            Assert.Same(failedResult, returned);
            Assert.True(httpContext.Items.ContainsKey("ValidationResult"));
            Assert.Same(failedResult, httpContext.Items["ValidationResult"]);
        }

        [Fact]
        public void AfterAspNetValidation_WithPassingResult_DoesNotStashAnything()
        {
            var interceptor = new ValidatorInterceptor();
            var httpContext = new DefaultHttpContext();
            var actionContext = CreateActionContext(httpContext);
            var passingResult = new FluentValidationResult();

            var returned = interceptor.AfterAspNetValidation(actionContext, Mock.Of<IValidationContext>(), passingResult);

            Assert.Same(passingResult, returned);
            Assert.False(httpContext.Items.ContainsKey("ValidationResult"));
        }

        [Fact]
        public void BeforeAspNetValidation_ReturnsCommonContextUnchanged()
        {
            var interceptor = new ValidatorInterceptor();
            var actionContext = CreateActionContext();
            var commonContext = Mock.Of<IValidationContext>();

            var result = interceptor.BeforeAspNetValidation(actionContext, commonContext);

            Assert.Same(commonContext, result);
        }

        [Fact]
        public void BeforeMvcValidation_ReturnsValidationContextUnchanged()
        {
            var interceptor = new ValidatorInterceptor();
            var controllerContext = new ControllerContext(CreateControllerActionContext());
            var validationContext = new ValidationContext(new object());

            var result = interceptor.BeforeMvcValidation(controllerContext, validationContext);

            Assert.Same(validationContext, result);
        }
    }
}
