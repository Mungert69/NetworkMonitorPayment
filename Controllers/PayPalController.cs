using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using NetworkMonitor.Payment.Services;
using NetworkMonitor.Objects.Factory;
using NetworkMonitor.Objects.ServiceMessage;
using MetroLog;
namespace NetworkMonitor.Payment.Controllers
{
    public class PayPalController
    {
        public readonly IOptions<PaymentOptions> options;
        private IStripeService _stripeService;
        private ILogger _logger;
        // Constructor witch same parameters ad PaymentsConroller
        public PayPalController(IOptions<PaymentOptions> options, IStripeService stripeService, INetLoggerFactory loggerFactory)
        {
            _logger = loggerFactory.GetLogger("PayPalController");
            _stripeService = stripeService;
            this.options = options;
        }
       
        Dictionary<string, string> baseURL = new Dictionary<string, string>()
{
    { "sandbox", "https://api-m.sandbox.paypal.com" },
    { "production", "https://api-m.paypal.com" }
};
        async Task<object> CreateOrder()
        {
            string accessToken = await GenerateAccessToken();
            string url = $"{baseURL["sandbox"]}/v2/checkout/orders";
            var payload = new
            {
                Method = "POST",
                Headers = new
                {
                    Content_Type = "application/json",
                    Authorization = $"Bearer {accessToken}"
                },
                Body = new
                {
                    Intent = "CAPTURE",
                    Purchase_units = new List<object>()
            {
                new
                {
                    Amount = new
                    {
                        Currency_code = "USD",
                        Value = "100.00"
                    }
                }
            }
                }
            };
            using var client = new HttpClient();
            var jsonPayload = JsonConvert.SerializeObject(payload);
            var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, httpContent);
            var json = await response.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject(json);
            return data;
        }
        async Task<object> CapturePayment(string orderId)
        {
            string accessToken = await GenerateAccessToken();
            string url = $"{baseURL["sandbox"]}/v2/checkout/orders/{orderId}/capture";
            var payload = new
            {
                Method = "POST",
                Headers = new
                {
                    Content_Type = "application/json",
                    Authorization = $"Bearer {accessToken}"
                }
            };
            using var client = new HttpClient();
            var jsonPayload = JsonConvert.SerializeObject(payload);
            var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, httpContent);
            var json = await response.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject(json);
            return data;
        }
        async Task<string> GenerateAccessToken()
        {
            string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{this.options.Value.PayPalClientID}:{this.options.Value.PayPalSecret}"));
            var httpClient = new HttpClient();
            var content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
            var response = await httpClient.PostAsync($"{baseURL["sandbox"]}/v1/oauth2/token", content);
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync();
            dynamic data = JsonConvert.DeserializeObject(responseBody);
            return data.access_token;
        }
    }
}