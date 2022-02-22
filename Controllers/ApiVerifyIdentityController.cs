using AA.DIDApi.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Text;

namespace AA.DIDApi.Controllers
{
    [Route("api/verifyIdentity")]
    [ApiController]
    public class ApiVerifyIdentityController : ApiBaseVCController
    {
        private readonly ILogger<ApiVerifyIdentityController> _logger;

        public ApiVerifyIdentityController(
            IConfiguration configuration,
            IOptions<AppSettingsModel> appSettings,
            IMemoryCache memoryCache,
            IWebHostEnvironment env,
            ILogger<ApiVerifyIdentityController> log) : base(configuration, appSettings, memoryCache, env, log)
        {
        }

        /// <summary>
        /// https://go-help.acuant.com/en/articles/5746430-pass-field-information-to-a-journey-through-a-url
        /// </summary>
        [HttpGet]
        public IActionResult RedirectToAcuantWorkflow()
        {
            var sessionGuid = HttpContext.Session.GetString("accountNameForUser");
            if (string.IsNullOrEmpty(sessionGuid))
            {
                sessionGuid = Guid.NewGuid().ToString();
                HttpContext.Session.SetString("accountNameForUser", sessionGuid);
            }
            
            byte[] toEncodeAsBytes = Encoding.ASCII.GetBytes(sessionGuid.ToString());
            var base64Encoded = Convert.ToBase64String(toEncodeAsBytes);

            CacheObjectWithExpiration(base64Encoded, sessionGuid);
            
            return new RedirectResult($"https://ultrapass-1.acuantgo-prod.com/?data={base64Encoded}");
        }
    }
}
