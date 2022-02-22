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
        private const string SessionKey = "AccountNameForUser";

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
            string man = HttpContext.Session.GetString(SessionKey);
            if (string.IsNullOrEmpty(man))
            {
                man = Guid.NewGuid().ToString("N");
                HttpContext.Session.SetString(SessionKey, man);
            }

            //object data = new { man = man };
            string dataSerialized = Newtonsoft.Json.JsonConvert.SerializeObject($"man:{man}"); // man:40b7b81827ef4d2e959167a24515d034
            byte[] toEncodeAsBytes = Encoding.ASCII.GetBytes(dataSerialized);
            var base64Encoded = Convert.ToBase64String(toEncodeAsBytes); // Im1hbjpiYmEzN2I1YmU0OWI0MWJlYTUyMmU0NzYyOGZjNzA1OSI=

            CacheObjectWithExpiration(base64Encoded, man);
            
            return new RedirectResult($"https://ultrapass-1.acuantgo-prod.com/?data={base64Encoded}");
        }

        #region Acuant

        [HttpGet("acuant/accepted")]
        public ActionResult PostAcuantAcceptedAsync([FromQuery] string urlParameter)
        {
            return BaseAcuantRedirectHandler("accepted", urlParameter: urlParameter);
        }

        [HttpGet("acuant/denied")]
        public ActionResult PostAcuantDeniedAsync([FromQuery] string urlParameter)
        {
            return BaseAcuantRedirectHandler("denied", urlParameter: urlParameter);
        }

        [HttpGet("acuant/manual")]
        public ActionResult GetAcuantManual([FromQuery] string urlParameter)
        {
            return BaseAcuantRedirectHandler(urlParameter, "manual");
        }

        [HttpGet("acuant/repeated")]
        public ActionResult PostAcuantRepeatedAsync([FromQuery] string urlParameter)
        {
            return BaseAcuantRedirectHandler("repeated", urlParameter: urlParameter);
        }

        [HttpPost("acuant/webhook")]
        public ActionResult PostAcuantWebhookAsync([FromQuery] string urlParameter)
        {
            string body = GetRequestBody("acuant/webhook");

            // read response
            // cross-reference with SessionKey

            return BaseAcuantRedirectHandler("webhook", urlParameter, body);
        }

        private ActionResult BaseAcuantRedirectHandler(
            string redirectLocation,
            string urlParameter = null,
            string body = null)
        {
            StringBuilder log = new StringBuilder($"{DateTime.UtcNow:o} -> {redirectLocation} -> {Request.Method} {Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString} ");
            if (!string.IsNullOrEmpty(urlParameter))
            {
                log.Append($" UrlParameter: '{urlParameter}'");
            }
            if (!string.IsNullOrEmpty(body))
            {
                log.Append($" Body: '{body}'");
            }

            Logger.LogInformation(log.ToString());
            return Redirect("http://localhost:5002/acuant/" + redirectLocation + ".html");
            //return Redirect("https://upid-vcapi.azurewebsites.net/acuant/" + redirectLocation + ".html");
        }

        #endregion Acuant
    }
}
