using Newtonsoft.Json.Linq;
using Stripe.Identity;

namespace AA.DIDApi.Models
{
    public class StripeVerifiedOutput
    {
        public StripeVerifiedOutput()
        { }

        public StripeVerifiedOutput(VerificationSession verificationSession)
        {
            JObject jObject = JObject.Parse(verificationSession.ToJson());

            var output = jObject["verified_outputs"];
            var address = output["address"];

            FirstName = output["first_name"].ToString();
            LastName = output["last_name"].ToString();

            AddressLine1 = address["line1"].ToString();
            AddressLine2 = address["line2"].ToString();
            City = address["city"].ToString();
            State = address["state"].ToString();
            PostalCode = address["postal_code"].ToString();
        }

        public string FirstName { get; set; }

        public string LastName { get; set; }

        public string AddressLine1 { get; set; }

        public string AddressLine2 { get; set; }

        public string City { get; set; }

        public string State { get; set; }

        public string PostalCode { get; set; }
    }
}
