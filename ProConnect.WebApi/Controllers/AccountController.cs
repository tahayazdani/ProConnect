using Microsoft.AspNetCore.Mvc;

namespace ProConnect.WebApi.Controllers
{
    public class AccountController : BaseController
    {
        protected readonly ILogger<AccountController> _logger;
        public AccountController(Logger<AccountController> logger)
        {
            _logger = logger;
        }

        #region Monitoring
        [HttpGet]
        [Route("ping")]
        public IActionResult Ping()
        {
            _logger.LogInformation("Ping request");
            return Ok(new { correlationToken = CorrelationToken });
        }
        #endregion
    }
}
