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
        private readonly IUserService _userService;
        private readonly ProConnectConfig _proConnectConfig;

        public UserController(ILogger<UserController> logger,
                              IUserService userService,
                              ProConnectConfig proConnectConfig)
        {
            _logger = logger;
            _userService = userService;
            _proConnectConfig = proConnectConfig;
        }


        #region SignIn
        /// <summary>
        /// this endpoint generates html content and redirect the user to LinkedIn login page for consent
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("generateconsentlinkedinloginpage")]
        [AllowAnonymous]
        public async Task<IActionResult> GenerateConsentLinkedInLogInPage()
        {
            var response = await _userService.GenerateConsentLinkedInLogInPage();
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
        [Route("linkedinauthentication")]
        [AllowAnonymous]
        public async Task<IActionResult> LinkedInAuthentication(string code, string state)
        {
            var response = await _userService.LinkedInAuthentication(code, state);
            return StatusCode(response.statuscode, response);
        }

        /// <summary>
        /// Returns the profile of the currently authenticated user, validating the bearer token.
        /// </summary>
        [HttpGet]
        [Route("systemauthentication")]
        public async Task<IActionResult> SystemAuthentication()
        {
            var response = await _userService.SystemAuthentication(CurrentUserId);
            return StatusCode(response.statuscode, response);
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
        [AllowAnonymous]
        public IActionResult LinkedInSignUp()
        {
            return Ok("https://www.linkedin.com/signup");
        }

        [HttpPost]
        [Route("signup")]
        [AllowAnonymous]
        public async Task<IActionResult> SignUp(SignUpRequest request)
        {
            var response = await _userService.SignUp(request);
            return StatusCode(response.statuscode, response);
        }

        #endregion

        #region Monitoring
        [HttpGet]
        [Route("ping")]
        [AllowAnonymous]
        public IActionResult Ping()
        {
            _logger.LogInformation("Ping request");
            return Ok(new { correlationToken = CorrelationToken });
        }
        #endregion
    }
}
