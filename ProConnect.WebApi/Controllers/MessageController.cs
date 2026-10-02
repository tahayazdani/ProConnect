using Microsoft.AspNetCore.Mvc;
using ProConnect.Application.Interface;
using ProConnect.Domain.Request;
using System;

namespace ProConnect.WebApi.Controllers
{
    public class MessageController : BaseController
    {
        protected readonly ILogger<MessageController> _logger;
        private readonly IMessageService _messageService;

        public MessageController(ILogger<MessageController> logger, IMessageService messageService)
        {
            _logger = logger;
            _messageService = messageService;
        }

        [HttpGet]
        [Route("{matchId}")]
        public async Task<IActionResult> GetMessages(Guid matchId, [FromQuery] MessagePageRequest request)
        {
            var response = await _messageService.GetMessages(CurrentUserId, matchId, request);
            return StatusCode(response.statuscode, response);
        }

        [HttpPost]
        [Route("{matchId}")]
        public async Task<IActionResult> SendMessage(Guid matchId, MessageRequest request)
        {
            var response = await _messageService.SendMessage(CurrentUserId, matchId, request);
            return StatusCode(response.statuscode, response);
        }
    }
}
