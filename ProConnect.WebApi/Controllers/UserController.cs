using Microsoft.AspNetCore.Mvc;

namespace ProConnect.WebApi.Controllers
{
    public class UserController : BaseController
    {
        protected readonly ILogger<UserController> _logger;
        public UserController(ILogger<UserController> logger)
        {
            _logger = logger;
        }


        #region SignIn
        [HttpPost]
        [Route("signin")]
        public IActionResult SignIn()
        {
            return Ok();
        }

        [HttpPost]
        [Route("logout")]
        public IActionResult Logout()
        {
            return Ok();
        }

        #endregion

        #region SignUp

        [HttpGet]
        [Route("linkedinsignup")]
        public IActionResult LinkedInSignUp()
        {
            return Ok("https://www.linkedin.com/signup");
        }

        [HttpPost]
        [Route("signup")]
        public IActionResult SignUp()
        {
            return Ok();
        }

        #endregion

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
