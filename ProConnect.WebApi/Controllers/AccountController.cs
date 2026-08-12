using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProConnect.Application.Interface;
using ProConnect.Domain.Request;

namespace ProConnect.WebApi.Controllers
{
    public class AccountController : BaseController
    {
        protected readonly ILogger<AccountController> _logger;
        private readonly IUserService _userService;

        public AccountController(ILogger<AccountController> logger, IUserService userService)
        {
            _logger = logger;
            _userService = userService;
        }

        #region Profile

        [HttpGet]
        [Route("profile")]
        public async Task<IActionResult> GetProfile()
        {
            var response = await _userService.GetProfile(CurrentUserId);
            return StatusCode(response.statuscode, response);
        }

        [HttpPut]
        [Route("profile")]
        public async Task<IActionResult> UpdateProfile(UpdateProfileRequest request)
        {
            var response = await _userService.UpdateProfile(CurrentUserId, request);
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
