using AA.DIDApi.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Stripe;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace AA.DIDApi.Controllers
{
    public abstract class ApiBaseVCController : ControllerBase
    {
        protected IMemoryCache Cache;
        protected readonly IWebHostEnvironment Environment;
        protected readonly AppSettingsModel AppSettings;
        protected readonly IConfiguration Configuration;
        protected readonly ILogger<ApiBaseVCController> Logger;
        private string _apiEndpoint;
        private string _authority;
        public string _apiKey;

        public ApiBaseVCController(
            IConfiguration configuration,
            IOptions<AppSettingsModel> appSettings,
            IMemoryCache memoryCache,
            IWebHostEnvironment env,
            ILogger<ApiBaseVCController> log)
        
        {
            AppSettings = appSettings.Value;
            Cache = memoryCache;
            Environment = env;
            Configuration = configuration;
            Logger = log;

            _apiEndpoint = string.Format(AppSettings.ApiEndpoint, AppSettings.TenantId);
            _authority = string.Format(AppSettings.Authority, AppSettings.TenantId);

            //_apiKey = System.Environment.GetEnvironmentVariable("INMEM-API-KEY");

            // TEST:
            StripeConfiguration.ApiKey = "sk_test_51KdMQHDEkM46zq0JFGA292fAcddC0usfsxn0X5SrXRmY701Er0eu5FilfliL16np8wcJlRhz8Wkc539ElWRJREOC00hBSdr26q";

            // LIVE
            //StripeConfiguration.ApiKey = "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";
        }

        protected string GetRequestHostName()
        {
            string scheme = "https"; // : this.Request.Scheme;
            string originalHost = Request.Headers["x-original-host"];

            return !string.IsNullOrEmpty(originalHost)
                ? $"{scheme}://{originalHost}"
                : $"{scheme}://{Request.Host}";
        }

        // return 400 error-message
        protected ActionResult ReturnErrorMessage(string errorMessage)
        {
            return BadRequest(new
                {
                    error = "400",
                    error_description = errorMessage
                });
        }

        // return 200 json 
        protected ActionResult ReturnJson(string json)
        {
            return new ContentResult { ContentType = "application/json", Content = json };
        }

        protected async Task<(string, string)> GetAccessToken()
        {
            IConfidentialClientApplication app =
                ConfidentialClientApplicationBuilder
                    .Create(AppSettings.ClientId)
                    .WithClientSecret(AppSettings.ClientSecret)
                    .WithAuthority(new Uri(_authority))
                    .Build();

            string[] scopes = new string[] { AppSettings.scope };
            AuthenticationResult result;

            try
            {
                result = await app.AcquireTokenForClient(scopes).ExecuteAsync();
            }
            catch (Exception ex)
            {
                return (string.Empty, ex.Message);
            }

            Logger.LogTrace(result.AccessToken);
            return (result.AccessToken, string.Empty);
        }

        // POST to VC Client API
        protected bool HttpPost(string body, out HttpStatusCode statusCode, out string response)
        {
            response = null;
            var accessToken = GetAccessToken().Result;
            if (accessToken.Item1 == string.Empty)
            {
                statusCode = HttpStatusCode.Unauthorized;
                response = accessToken.Item2;
                return false;
            }

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Item1);
            try
            {
                using HttpResponseMessage res = client.PostAsync(_apiEndpoint, new StringContent(body, Encoding.UTF8, "application/json")).Result;
                response = res.Content.ReadAsStringAsync().Result;
                statusCode = res.StatusCode;
                return res.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                response = ex.Message;
                statusCode = HttpStatusCode.InternalServerError;
                return false;
            }
        }

        protected bool HttpGet(string url, out HttpStatusCode statusCode, out string response)
        {
            using HttpClient client = new HttpClient();
            using HttpResponseMessage res = client.GetAsync(url).Result;
            response = res.Content.ReadAsStringAsync().Result;
            statusCode = res.StatusCode;
            return res.IsSuccessStatusCode;
        }

        protected void TraceHttpRequest()
        {
            string xForwardedFor = Request.Headers["X-Forwarded-For"];
            string ipaddr = !string.IsNullOrEmpty(xForwardedFor)
                ? xForwardedFor
                : HttpContext.Connection.RemoteIpAddress.ToString();

            Logger.LogTrace($"{DateTime.UtcNow:o} {ipaddr} -> {Request.Method} {Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}");
        }

        protected string GetRequestBody(string methodName)
        {
            using StreamReader reader = new StreamReader(Request.Body);
            var body = reader.ReadToEndAsync().Result.Replace("\r\n", string.Empty);
            Logger.LogInformation($"{methodName}: {body}");

            return body;
        }

        protected bool GetCachedObject<T>(string key, out T Object)
        {
            Object = default;
            bool rc;
            if (rc = Cache.TryGetValue(key, out object val))
            {
                Object = (T)Convert.ChangeType(val, typeof(T));
            }

            return rc;
        }

        protected bool GetCachedValue(string key, out string value)
        {
            return Cache.TryGetValue(key, out value);
        }

        protected void CacheObjectWithExpiration(string key, object Object)
        {
            Cache.Set(key, Object, DateTimeOffset.Now.AddSeconds(AppSettings.CacheExpiresInSeconds));
        }

        protected void CacheValueWithNoExpiration(string key, string value)
        {
            Cache.Set(key, value);
        }

        protected void RemoveCacheValue(string key)
        {
            Cache.Remove(key);
        }
    }
}
