using AA.DIDApi.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Stripe;
using Stripe.Identity;
using System.Collections.Generic;

namespace AA.DIDApi.Controllers
{
    [Route("api/verifyIdentity")]
    [ApiController]
    public class ApiVerifyIdentityController : ApiBaseVCController
    {
        //private const string SessionKey = "AccountNameForUser";
        //private const string SessionKeyEncoded = "AccountNameForUserEncoded";

        public ApiVerifyIdentityController(
            IConfiguration configuration,
            IOptions<AppSettingsModel> appSettings,
            IMemoryCache memoryCache,
            IWebHostEnvironment env,
            ILogger<ApiVerifyIdentityController> log) : base(configuration, appSettings, memoryCache, env, log)
        {
        }

        #region Stripe

        /// <summary>
        /// Stripe implementation
        /// </summary>
        [HttpGet]
        public IActionResult RedirectToAcuantWorkflow()
        {
            string redirectUrl = "https://upid-vcapi.azurewebsites.net";
#if DEBUG
            redirectUrl = "http://localhost:5002";
#endif

            var options = new VerificationSessionCreateOptions
            {
                Type = "document",
                ReturnUrl = $"{redirectUrl}/issuer.html",
                Options = new VerificationSessionOptionsOptions 
                {
                    Document = new VerificationSessionOptionsDocumentOptions
                    {
                        //RequireMatchingSelfie = true,
                        RequireLiveCapture = true,
                        AllowedTypes = new List<string>
                        {
                            "driving_license",
                            //"passport",
                            //"is_number"
                        },
                    }
                }
            };
            var service = new VerificationSessionService();
            var verificationSession = service.Create(options);
            HttpContext.Session.SetString("upid-stripe-session", verificationSession.Id);

            return new RedirectResult(verificationSession.Url);
        }

        [HttpPost("stripe/webhook")]
        public IActionResult PostStripeWebhookAsync()
        {
            string body = GetRequestBody("stripe/webhook");
            //JObject json = JObject.Parse(body);

            VerificationSession response = VerificationSession.FromJson(body);
                        
            switch (response.Type)
            {
                case "identity.verification_session.verified":
                    //var id = json["data"]["object"]["id"].Value<string>();
                    //VerificationSessionService svc = new VerificationSessionService(StripeConfiguration.StripeClient);
                    //VerificationSession userVerificationSession = 
                    //    svc.Get(id, options: new VerificationSessionGetOptions
                    //        {
                    //            Expand = new List<string> { "verified_outputs" }
                    //        });

                    //var stripeVerifiedOutput = new StripeVerifiedOutput(userVerificationSession);
                    //var stripeVerifiedOutputJson = Newtonsoft.Json.JsonConvert.SerializeObject(stripeVerifiedOutput);
                    //HttpContext.Session.SetString("upid-stripe-verification-session", stripeVerifiedOutputJson);

                    break;
                    
                case "identity.verification_session.requires_input":
                    // TODO
                    break;
            }
            
            return Ok();
        }

        //private bool HttpPostStripe(string body, out HttpStatusCode statusCode, out string response)
        //{
        //    //response = null;
        //    //var accessToken = GetAccessToken().Result;
        //    //if (accessToken.Item1 == string.Empty)
        //    //{
        //    //    statusCode = HttpStatusCode.Unauthorized;
        //    //    response = accessToken.Item2;
        //    //    return false;
        //    //}

        //    using HttpClient client = new HttpClient();
        //    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Item1);
        //    using HttpResponseMessage res = client.PostAsync(_apiEndpoint, new StringContent(body, Encoding.UTF8, "application/json")).Result;
        //    response = res.Content.ReadAsStringAsync().Result;

        //    statusCode = res.StatusCode;
        //    return res.IsSuccessStatusCode;
        //}

        #endregion Stripe

        #region Acuant

        /*
        /// <summary>
        /// https://go-help.acuant.com/en/articles/5746430-pass-field-information-to-a-journey-through-a-url
        /// </summary>
        [HttpGet]
        public IActionResult RedirectToAcuantWorkflow()
        {
            string man = HttpContext.Session.GetString(SessionKey);
            if (string.IsNullOrEmpty(man))
            {
                man = Guid.NewGuid().ToString("N");                             // 70c2947e-e0ed-4823-80f2-d9bd3e149231
                HttpContext.Session.SetString(SessionKey, man);
            }

            byte[] toEncodeAsBytes = Encoding.ASCII.GetBytes($"man:{man:N}");   // man:70c2947ee0ed482380f2d9bd3e149231
            var base64Encoded = Convert.ToBase64String(toEncodeAsBytes);        // bWFuOjcwYzI5NDdlZTBlZDQ4MjM4MGYyZDliZDNlMTQ5MjMx

            CacheObjectWithExpiration(man, base64Encoded);
            HttpContext.Session.SetString(SessionKeyEncoded, base64Encoded);

            return new RedirectResult($"https://pvlultratst.acuantgo-prod.com/?data={base64Encoded}");
            //return new RedirectResult($"https://ultrapass-1.acuantgo-prod.com/?data={base64Encoded}"); // old form
        }

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
            const string acuantJwt = "h6k6o4em5p1j3r5r2h305u3e4p2e6k5v";
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

            string redirectUrl = "https://upid-vcapi.azurewebsites.net";
#if DEBUG
            redirectUrl = "http://localhost:5002";
#endif
            return Redirect($"{redirectUrl}/acuant/{redirectLocation}.html");
        }

        */

        #endregion Acuant
    }
}
