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

        // Temporary, reversible deactivation (Instagram-style "take a break") - NOT a delete, nothing is
        // removed. See IUserService.DeactivateAccount. Signing back in via LinkedIn reactivates the
        // account automatically - there's no separate "reactivate" endpoint by design. Deliberately a
        // dedicated action route (not DELETE on "profile") so it can't be confused with the real,
        // permanent delete below.
        [HttpPost]
        [Route("deactivate")]
        public async Task<IActionResult> DeactivateAccount()
        {
            var response = await _userService.DeactivateAccount(CurrentUserId);
            return StatusCode(response.statuscode, response);
        }

        // Permanent, irreversible hard delete of the account and every piece of data tied to it (swipes,
        // matches, match criteria) - NOT the same as DeactivateAccount above. See
        // IUserService.DeleteAccount. HTTP DELETE on "profile" is intentional here - this is the one
        // action that actually deletes the resource.
        [HttpDelete]
        [Route("profile")]
        public async Task<IActionResult> DeleteAccount()
        {
            var response = await _userService.DeleteAccount(CurrentUserId);
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
