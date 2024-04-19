using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ProConnect.Domain.Response;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Application.ErrorHandler
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;

        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _logger = logger;
            _next = next;
        }

        public async Task InvokeAsync(HttpContext httpContext)
        {
            try
            {
                await _next(httpContext);
            }
            catch (Exception ex)
            {
                //string text = Guid.NewGuid().ToString();
                //if (httpContext.Request != null)
                //{
                //    httpContext.Request.Headers.TryGetValue("correlation-token", out var value);
                //    text = ((!string.IsNullOrEmpty(value)) ? value.ToString() : text);
                //    httpContext.Response.Headers.Add("correlation-token", text);
                //}

                _logger.LogError(ex, "Something went wrong...");
                await HandleExceptionAsync(httpContext, ex);
            }
        }

        private static Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = 500;
            return context.Response.WriteAsync(JsonConvert.SerializeObject(new ProConnectResponse<string>
            {
                body = string.Empty,
                statuscode = context.Response.StatusCode,
                errormessage = exception.Message,
                message = $"Something went wrong, Please call administrator."
            }).ToString());
        }
    }
}
