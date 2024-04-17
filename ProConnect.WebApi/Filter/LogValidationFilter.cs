using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace ProConnect.WebApi.Filter
{
    public class LogValidationFilter : IResultFilter
    {
        protected readonly ILogger<LogValidationFilter> _logger;

        public LogValidationFilter(ILogger<LogValidationFilter> logger)
        {
            _logger = logger;
        }

        public void OnResultExecuting(ResultExecutingContext context)
        {

            if (!context.ModelState.IsValid)
            {
                _logger.LogWarning(JsonConvert.SerializeObject(context.Result));
            }

        }

        public void OnResultExecuted(ResultExecutedContext context)
        {
        }
    }
}