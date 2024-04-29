using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ProConnect.Application.Interface;
using ProConnect.Application.Validation;
using ProConnect.Domain.Config;
using ProConnect.Domain.Request;

namespace ProConnect.WebApi.Controllers
{
    public class UserController : BaseController
    {
        protected readonly ILogger<UserController> _logger;
        private readonly IUserService _user;
        private readonly ProxyCurlConfig _proxyCurlConfig;
        private readonly LinkedInApplicationConfig _linkedInApplicationConfig;

        public UserController(ILogger<UserController> logger,
                              IUserService user,
                              IOptions<ProxyCurlConfig> proxyCurlConfig,
                              IOptions<LinkedInApplicationConfig> linkedInApplicationConfig)
        {
            _logger = logger;
            _user = user;
            _proxyCurlConfig = proxyCurlConfig.Value;
            _linkedInApplicationConfig = linkedInApplicationConfig.Value;
        }


        #region SignIn
        /// <summary>
        /// this endpoint generates html content and redirect the user to LinkedIn login page for consent
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("generateconsentlinkedinloginpage")]
        public async Task<IActionResult> GenerateConsentLinkedInLogInPage()
        {
            var response = await _user.GenerateConsentLinkedInLogInPage(_linkedInApplicationConfig);
            return StatusCode(response.statuscode, response);
        }


        /// <summary>
        /// this endpoint will be triggered once the user give us consent to be redirected back to our website
        /// 1- then the /accessToken endpoint needs to be called to genereate the access token, 
        /// 2- then the we can extract list of claims : full name, email address, image, id
        /// </summary>
        /// <param name="code"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("authenticate")]
        public IActionResult Authenticate(string code, string state)
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
            var response = await _user.SignUp(request, _proxyCurlConfig);
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
