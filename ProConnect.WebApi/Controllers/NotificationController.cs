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

        [HttpDelete]
        [Route("devices/{device_token}")]
        public async Task<IActionResult> UnregisterDevice(string device_token)
        {
            var response = await _notificationService.UnregisterDevice(CurrentUserId, device_token);
            return StatusCode(response.statuscode, response);
        }
    }
}
