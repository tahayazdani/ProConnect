using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ProConnect.WebApi.Middleware;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Tests.WebApi.Middleware
{
    /// <summary>
    /// LoggingMiddleware.Invoke logs the request, swaps in a buffering MemoryStream for
    /// context.Response.Body so it can read+log whatever downstream writes, then copies that buffer
    /// back onto the original response stream and restores context.Response.Body before returning -
    /// callers after this middleware in the pipeline (and the real response to the client) must see
    /// the same bytes _next wrote, plus the stream swap must be invisible once Invoke returns.
    /// Moq's ILogger extension-method verification is awkward, so a minimal hand-written ILogger
    /// fake captures formatted messages directly instead.
    /// </summary>
    public class LoggingMiddlewareTests
    {
        private sealed class FakeLogger<T> : ILogger<T>
        {
            public List<string> Messages { get; } = new();

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                Messages.Add(formatter(state, exception));
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }

        private static HttpContext CreateContext(string requestBody, out MemoryStream originalResponseBody)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/api/test";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            originalResponseBody = new MemoryStream();
            context.Response.Body = originalResponseBody;
            return context;
        }

        [Fact]
        public async Task Invoke_CallsNextMiddleware()
        {
            var logger = new FakeLogger<LoggingMiddleware>();
            var nextCalled = false;
            RequestDelegate next = ctx => { nextCalled = true; return Task.CompletedTask; };
            var middleware = new LoggingMiddleware(next, logger);
            var context = CreateContext("{}", out _);

            await middleware.Invoke(context);

            Assert.True(nextCalled);
        }

        [Fact]
        public async Task Invoke_LogsRequestMethodPathAndBody()
        {
            var logger = new FakeLogger<LoggingMiddleware>();
            RequestDelegate next = _ => Task.CompletedTask;
            var middleware = new LoggingMiddleware(next, logger);
            var context = CreateContext("{\"ping\":\"pong\"}", out _);

            await middleware.Invoke(context);

            var requestLog = Assert.Single(logger.Messages, m => m.Contains("=== Request Info ==="));
            Assert.Contains("method = POST", requestLog);
            Assert.Contains("path = /api/test", requestLog);
            Assert.Contains("body = {\"ping\":\"pong\"}", requestLog);
        }

        [Fact]
        public async Task Invoke_RestoresRequestBodyPositionToZeroAfterReadingIt()
        {
            var logger = new FakeLogger<LoggingMiddleware>();
            RequestDelegate next = _ => Task.CompletedTask;
            var middleware = new LoggingMiddleware(next, logger);
            var context = CreateContext("{}", out _);

            await middleware.Invoke(context);

            Assert.Equal(0, context.Request.Body.Position);
        }

        [Fact]
        public async Task Invoke_LogsResponseBodyWrittenByNext()
        {
            var logger = new FakeLogger<LoggingMiddleware>();
            RequestDelegate next = async ctx =>
            {
                var bytes = Encoding.UTF8.GetBytes("{\"result\":\"ok\"}");
                await ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length);
            };
            var middleware = new LoggingMiddleware(next, logger);
            var context = CreateContext("{}", out _);

            await middleware.Invoke(context);

            var responseLog = Assert.Single(logger.Messages, m => m.Contains("=== Response Info ==="));
            Assert.Contains("body = {\"result\":\"ok\"}", responseLog);
        }

        [Fact]
        public async Task Invoke_CopiesResponseBackOntoOriginalStreamAndRestoresIt()
        {
            var logger = new FakeLogger<LoggingMiddleware>();
            RequestDelegate next = async ctx =>
            {
                var bytes = Encoding.UTF8.GetBytes("downstream-content");
                await ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length);
            };
            var middleware = new LoggingMiddleware(next, logger);
            var context = CreateContext("{}", out var originalResponseBody);

            await middleware.Invoke(context);

            Assert.Same(originalResponseBody, context.Response.Body);
            originalResponseBody.Position = 0;
            var finalContent = await new StreamReader(originalResponseBody).ReadToEndAsync();
            Assert.Equal("downstream-content", finalContent);
        }
    }
}
