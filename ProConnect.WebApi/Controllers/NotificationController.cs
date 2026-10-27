using Microsoft.AspNetCore.Mvc;
using ProConnect.Application.Interface;
using ProConnect.Domain.Request;
using System.Threading.Tasks;

namespace ProConnect.WebApi.Controllers
{
    /// <summary>
    /// Client-facing notification surface - see INotificationService remarks. Every action here requires
    /// the normal JWT auth (global fallback policy in Program.cs); there is no anonymous endpoint in this
    /// controller the way SubscriptionController.Webhook is, since nothing external calls into this surface
    /// directly - MatchService/SubscriptionService call INotificationService.Notify in-process instead.
    /// Purely REST: listing/paging/marking-read/device registration. The live push side (NotificationHub)
    /// is additive and lives in WebApi/Hubs, not here.
    /// </summary>
    public class NotificationController : BaseController
    {
        private readonly INotificationService _notificationService;

        public NotificationController(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        [HttpGet]
        public async Task<IActionResult> GetNotifications([FromQuery] NotificationPageRequest request)
        {
            var response = await _notificationService.GetNotifications(CurrentUserId, request);
            return StatusCode(response.statuscode, response);
        }

        [HttpGet]
        [Route("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var response = await _notificationService.GetUnreadCount(CurrentUserId);
            return StatusCode(response.statuscode, response);
        }

        [HttpPost]
        [Route("{notificationid}/read")]
        public async Task<IActionResult> MarkAsRead(System.Guid notificationid)
        {
            var response = await _notificationService.MarkAsRead(CurrentUserId, notificationid);
            return StatusCode(response.statuscode, response);
        }

        [HttpPost]
        [Route("read-all")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var response = await _notificationService.MarkAllAsRead(CurrentUserId);
            return StatusCode(response.statuscode, response);
        }

        [HttpPost]
        [Route("devices")]
        public async Task<IActionResult> RegisterDevice(RegisterDeviceTokenRequest request)
        {
            var response = await _notificationService.RegisterDevice(CurrentUserId, request);
            return StatusCode(response.statuscode, response);
        }

        // BUG-24 fix (G-26): a device_token containing a %2F does NOT get split across route segments -
        // verified live (diagnostic logging) that Kestrel binds the single {device_token} segment to the
        // literal, still-encoded text "...%2F..." without ever decoding it back into a '/' during routing.
        // So a plain {device_token} route was never the problem, and {*device_token} (tried first, left
        // here since it's harmless/backward compatible either way) doesn't fix anything on its own. The
        // actual mismatch is that the value NotificationService.UnregisterDevice receives is still
        // percent-encoded ("simple%2Fslash") while RegisterDevice/UpsertDeviceToken persisted the raw,
        // decoded token ("simple/slash") straight from the JSON body - see the %2F un-escape fix applied
        // in NotificationService.UnregisterDevice for the actual root-cause fix (BUGS_FOUND.md BUG-24 / G-26).
        [HttpDelete]
        [Route("devices/{*device_token}")]
        public async Task<IActionResult> UnregisterDevice(string device_token)
        {
            var response = await _notificationService.UnregisterDevice(CurrentUserId, device_token);
            return StatusCode(response.statuscode, response);
        }
    }
}
