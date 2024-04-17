using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

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
    }
}
