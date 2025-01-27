﻿using AA.DIDApi.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stripe;
using Stripe.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;

namespace AA.DIDApi.Controllers
{
    [Route("api/issuer")]
    [ApiController]
    public class ApiIssuerController : ApiBaseVCController
    {
        //private const string IssuanceRequestConfigFile = "%cd%\\requests\\issuance_request_config_v2.json";

        public ApiIssuerController(
            IConfiguration configuration,
            IOptions<AppSettingsModel> appSettings,
            IMemoryCache memoryCache,
            IWebHostEnvironment env,
            ILogger<ApiIssuerController> log) : base(configuration, appSettings, memoryCache, env, log)
        {
            GetIssuanceManifest();
        }

        #region Endpoints

        [HttpGet("echo")]
        public ActionResult Echo([FromQuery] string verificationSessionGuid)
        {
            TraceHttpRequest();

            try
            {
                JObject manifest = GetIssuanceManifest();
                Dictionary<string, string> claims = GetSelfAssertedClaims(manifest, verificationSessionGuid);
                var info = new
                {
                    date = DateTime.Now.ToString(),
                    host = GetRequestHostName(),
                    api = GetApiPath(),
                    didIssuer = manifest["input"]["issuer"],
                    credentialType = manifest["id"],
                    displayCard = manifest["display"]["card"],
                    buttonColor = "#000080",
                    contract = manifest["display"]["contract"],
                    selfAssertedClaims = claims
                };

                return ReturnJson(JsonConvert.SerializeObject(info));
            }
            catch (Exception ex)
            {
                return ReturnErrorMessage(ex.Message);
            }
        }

        private StripeVerifiedOutput GetLatestStripeVerifiedOutput(string verificationSessionGuid)
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString(STRIPE_DATA_VERIFIED_OUTPUT)))
            {
                var options = new VerificationSessionListOptions
                {
                    Limit = 25,
                    Expand = new List<string> { "data.verified_outputs" },
                };
                VerificationSessionService svc = new VerificationSessionService(StripeConfiguration.StripeClient);
                StripeList<VerificationSession> verificationSessions = svc.List(options);

                var verifiedStripeSession = verificationSessions.Data.SingleOrDefault(x => 
                    x.Metadata != null && 
                    x.Metadata.ContainsKey(STRIPE_VERIFICATION_SESSIONGUID) && 
                    x.Metadata[STRIPE_VERIFICATION_SESSIONGUID] == verificationSessionGuid);

                StripeVerifiedOutput stripeVerifiedOutput = null;
                if (verifiedStripeSession?.VerifiedOutputs != null)
                {
                    stripeVerifiedOutput = new StripeVerifiedOutput(verifiedStripeSession);
                    HttpContext.Session.SetString(STRIPE_DATA_VERIFIED_OUTPUT, JsonConvert.SerializeObject(stripeVerifiedOutput));
                }
                else
                {
                    return null;
                }
            }

            return JsonConvert.DeserializeObject<StripeVerifiedOutput>(HttpContext.Session.GetString(STRIPE_DATA_VERIFIED_OUTPUT));
        }

        [HttpGet]
        [Route("logo.png")]
        public ActionResult Logo()
        {
            TraceHttpRequest();
            JObject manifest = GetIssuanceManifest();
            return Redirect(manifest["display"]["card"]["logo"]["uri"].ToString());
        }

        [HttpGet("issue-request")]
        public ActionResult GetIssuanceReference()
        {
            TraceHttpRequest();
            try
            {
                string correlationId = Guid.NewGuid().ToString();
                VCIssuanceRequest request = new VCIssuanceRequest()
                {
                    includeQRCode = false,
                    authority = AppSettings.VerifierAuthority,
                    registration = new Registration()
                    {
                        clientName = AppSettings.client_name
                    },
                    callback = new Callback()
                    {
                        url = $"{GetApiPath()}/issue-callback",
                        state = correlationId,
                        headers = new Dictionary<string, string>() { { "api-key", _apiKey } }
                    },
                    issuance = new Issuance()
                    {
                        type = AppSettings.CredentialType,
                        manifest = AppSettings.DidManifest,
                        pin = null
                    }
                };

                // if pincode is required, set it up in the request
                if (AppSettings.IssuancePinCodeLength > 0)
                {
                    int pinCode = RandomNumberGenerator.GetInt32(1, int.Parse("".PadRight(AppSettings.IssuancePinCodeLength, '9')));
                    Logger.LogTrace("pin={0}", pinCode);
                    request.issuance.pin = new Pin()
                    {
                        length = AppSettings.IssuancePinCodeLength,
                        value = string.Format("{0:D" + AppSettings.IssuancePinCodeLength.ToString() + "}", pinCode)
                    };
                }

                // set self-asserted claims passed as query string parameters
                // This sample assumes that ALL claims comes from the UX
                JObject manifest = GetIssuanceManifest();
                Dictionary<string, string> claims = GetSelfAssertedClaims(manifest);
                if (claims.Count > 0)
                {
                    request.issuance.claims = new Dictionary<string, string>();
                    foreach (KeyValuePair<string, string> kvp in claims)
                    {
                        request.issuance.claims.Add(kvp.Key, Request.Query[kvp.Key].ToString());
                    }
                }

                var settings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                };
                string claimsString = JsonConvert.SerializeObject(request.issuance.claims, Formatting.None, settings);
                Logger.LogTrace($"VC Client API Request Claims\n{claimsString}");
                string jsonString = JsonConvert.SerializeObject(request, Formatting.None, settings);
                Logger.LogTrace($"VC Client API Request\n{jsonString}");

                string contents = "";
                HttpStatusCode statusCode = HttpStatusCode.OK;
                if (!HttpPost(jsonString, out statusCode, out contents))
                {
                    Logger.LogError($"VC Client API Error Response\n{contents}");
                    return ReturnErrorMessage(contents);
                }

                // add the id and the pin to the response we give the browser since they need them
                JObject requestConfig = JObject.Parse(contents);
                if (AppSettings.IssuancePinCodeLength > 0)
                {
                    requestConfig["pin"] = request.issuance.pin.value;
                }

                requestConfig.Add(new JProperty("id", correlationId));
                jsonString = JsonConvert.SerializeObject(requestConfig);
                Logger.LogTrace($"VC Client API Response\n{jsonString}");
                return ReturnJson(jsonString);
            }
            catch (Exception ex)
            {
                return ReturnErrorMessage(ex.Message);
            }
        }

        [HttpPost("issue-callback")]
        public ActionResult IssuanceCallbackModel()
        {
            TraceHttpRequest();
            try
            {
                string body = GetRequestBody("issue-callback");
                Request.Headers.TryGetValue("api-key", out var apiKey);
                if (_apiKey != apiKey)
                {
                    return new ContentResult()
                    {
                        StatusCode = (int)HttpStatusCode.Unauthorized,
                        Content = "api-key wrong or missing"
                    };
                }

                VCCallbackEvent callback = JsonConvert.DeserializeObject<VCCallbackEvent>(body);
                CacheObjectWithExpiration(callback.state, callback);
                return new OkResult();
            }
            catch (Exception ex)
            {
                return ReturnErrorMessage(ex.Message);
            }
        }

        [HttpGet("issue-response")]
        public ActionResult IssuanceResponseModel()
        {
            TraceHttpRequest();
            try
            {
                string correlationId = Request.Query["id"];
                if (string.IsNullOrEmpty(correlationId))
                {
                    return ReturnErrorMessage("Missing argument 'id'");
                }

                if (GetCachedObject<VCCallbackEvent>(correlationId, out VCCallbackEvent callback))
                {
                    if (callback.code == "request_retrieved")
                    {
                        return ReturnJson(JsonConvert.SerializeObject(new { status = 1, message = "QR Code is scanned. Waiting for issuance to complete." }));
                    }

                    if (callback.code == "issuance_successful")
                    {
                        RemoveCacheValue(correlationId);
                        return ReturnJson(JsonConvert.SerializeObject(new { status = 2, message = "Issuance process is completed" }));
                    }

                    if (callback.code == "issuance_failed")
                    {
                        RemoveCacheValue(correlationId);
                        return ReturnJson(JsonConvert.SerializeObject(new { status = 99, message = "Issuance process failed with reason: " + callback.error.message }));
                    }
                }
                return new OkResult();
            }
            catch (Exception ex)
            {
                return ReturnErrorMessage(ex.Message);
            }
        }

        #endregion Endpoints

        #region Helpers

        private string GetApiPath()
        {
            return $"{GetRequestHostName()}/api/issuer";
        }

        private JObject GetIssuanceManifest()
        {
            if (GetCachedValue("manifestIssuance", out string json))
            {
                return JObject.Parse(json);
            }

            // download manifest and cache it
            if (!HttpGet(AppSettings.DidManifest, out HttpStatusCode statusCode, out string contents))
            {
                Logger.LogError($"HttpStatus {statusCode} fetching manifest {AppSettings.DidManifest}");
                return null;
            }

            CacheValueWithNoExpiration("manifestIssuance", contents);
            return JObject.Parse(contents);
        }

        private Dictionary<string, string> GetSelfAssertedClaims(JObject manifest, string verificationSessionGuid = null)
        {
            Dictionary<string, string> claims = new Dictionary<string, string>();
            if (manifest["input"]["attestations"]["idTokens"][0]["id"].ToString() == "https://self-issued.me")
            {
                StripeVerifiedOutput verifiedOutput = GetLatestStripeVerifiedOutput(verificationSessionGuid);

                foreach (var claim in manifest["input"]["attestations"]["idTokens"][0]["claims"])
                {
                    string propertyValue = null;
                    if (verifiedOutput != null)
                    {
                        switch (claim["claim"].ToString())
                        {
                            case "$.given_name":
                                propertyValue = verifiedOutput.FirstName;
                                break;
                            case "$.family_name":
                                propertyValue = verifiedOutput.LastName;
                                break;
                            case "$.address":
                                propertyValue = string.Join(",", verifiedOutput.AddressLine1, verifiedOutput.AddressLine2).TrimEnd(',');
                                break;
                            case "$.state":
                                propertyValue = verifiedOutput.State;
                                break;
                            case "$.zipcode":
                                propertyValue = verifiedOutput.PostalCode;
                                break;
                        }
                    }
                    claims.Add(claim["claim"].ToString(), propertyValue ?? null);
                }
            }

            return claims;
        }

        #endregion Helpers
    }
}
