using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
        [EnableRateLimiting("strict")]
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
        [EnableRateLimiting("strict")]
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


        /// <summary>
        /// Redeems a still-valid refresh token (issued alongside the access token by SignUp/
        /// LinkedInAuthentication - see UserService.IssueTokenPair) for a brand-new access+refresh pair,
        /// rotating the presented one out so it can't be redeemed again. Deliberately [AllowAnonymous]:
        /// the whole point is to let a client whose access token has already expired get a new one
        /// without having to go through LinkedIn sign-in again - requiring a valid bearer token here would
        /// defeat that. The refresh token value itself is the credential being checked, by
        /// UserService.RefreshToken.
        /// </summary>
        [HttpPost]
        [Route("refreshtoken")]
        [AllowAnonymous]
        [EnableRateLimiting("strict")]
        public async Task<IActionResult> RefreshToken(RefreshTokenRequest request)
        {
            var response = await _userService.RefreshToken(request?.refreshtoken);
            return StatusCode(response.statuscode, response);
        }

        /// <summary>
        /// Revokes the given refresh token so it can no longer be redeemed via /refreshtoken. Same
        /// [AllowAnonymous] reasoning as /refreshtoken above - a client logging out because its access
        /// token already expired shouldn't need a valid one just to tell the server to forget its refresh
        /// token. Body is optional (missing/empty refreshtoken is a no-op success, see
        /// UserService.Logout) so existing callers that don't send one yet don't start failing.
        /// </summary>
        [HttpPost]
        [Route("logout")]
        [AllowAnonymous]
        public async Task<IActionResult> Logout(RefreshTokenRequest request)
        {
            var response = await _userService.Logout(request?.refreshtoken);
            return StatusCode(response.statuscode, response);
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
        [EnableRateLimiting("strict")]
        public async Task<IActionResult> SignUp(SignUpRequest request)
        {
            var response = await _userService.SignUp(request);
            return StatusCode(response.statuscode, response);
        }

        #endregion

        #region Profile

        /// <summary>
        /// Scrapes and returns a LinkedIn profile by public identifier, without signing anyone up.
        /// Kept behind auth (no [AllowAnonymous]) since every call costs money against the scraper API.
        /// </summary>
        [HttpGet]
        [Route("linkedinprofile")]
        [EnableRateLimiting("strict")]
        public async Task<IActionResult> GetLinkedInProfile(string publicIdentifier)
        {
            var response = await _userService.GetLinkedInProfile(publicIdentifier);
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
