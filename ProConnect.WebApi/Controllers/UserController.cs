using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ProConnect.Application.Interface;
using ProConnect.Domain.Config;
using ProConnect.Domain.Request;

namespace ProConnect.WebApi.Controllers
{
    public class UserController : BaseController
    {
        protected readonly ILogger<UserController> _logger;
        private readonly IUserService _user;
        private readonly IOptions<ProxyCurlConfig> _proxyCurlConfig;

        public UserController(ILogger<UserController> logger,
                              IUserService user,
                              IOptions<ProxyCurlConfig> proxyCurlConfig)
        {
            _logger = logger;
            _user = user;
            _proxyCurlConfig = proxyCurlConfig;
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
        [AllowAnonymous]
        public async Task<IActionResult> SignUp(SignUpRequest request)
        {
            var response = await _user.SignUp(request, _proxyCurlConfig.Value);
            return StatusCode(response.statuscode, response);
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
