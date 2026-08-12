using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.Http.Headers;
using System.Security.Claims;

namespace ProConnect.WebApi.Controllers
{
    [Route("api/[controller]/v{version:apiVersion}")]
    [ApiController]
    [ApiVersion("1.0")]
    public class BaseController : ControllerBase
    {
        public BaseController()
        {
        }

        private string _token = null;
        public string CorrelationToken
        {
            get
            {
                if (string.IsNullOrEmpty(_token))
                {
                    HttpContext.Request.Headers.TryGetValue("correlation-token", out var correlationToken);
                    _token = !string.IsNullOrEmpty(correlationToken.ToString()) ? correlationToken.ToString() : System.Guid.NewGuid().ToString();
                }
                return _token;
            }
        }

        private Guid? _currentUserId = null;
        public Guid CurrentUserId
        {
            get
            {
                if (_currentUserId == null)
                {
                    var claimValue = HttpContext.User?.FindFirst("userid")?.Value;
                    _currentUserId = Guid.TryParse(claimValue, out var userId) ? userId : Guid.Empty;
                }
                return _currentUserId.Value;
            }
        }
    }
}
