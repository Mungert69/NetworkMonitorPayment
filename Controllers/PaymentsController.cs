using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using NetworkMonitor.Payment.Services;
using NetworkMonitor.Objects.Factory;
using NetworkMonitor.Payment.Models;
using MetroLog;
namespace NetworkMonitor.Payment.Controllers
{
    public class PaymentsController : Controller
    {
        public readonly IOptions<PaymentOptions> options;
        private readonly IStripeClient client;
        private IStripeService _stripeService;
        private ILogger _logger;
        public PaymentsController(IOptions<PaymentOptions> options, IStripeService stripeService, INetLoggerFactory loggerFactory)
        {
            _logger = loggerFactory.GetLogger("PaymentsController");
            _stripeService = stripeService;
            this.options = options;
            this.client = new StripeClient(this.options.Value.SecretKey);
        }
        [HttpGet("config")]
        public ConfigResponse Setup()
        {
            return new ConfigResponse
            {
                ProPrice = this.options.Value.Products[0].PriceId,
                BasicPrice = this.options.Value.Products[1].PriceId,
                PublishableKey = this.options.Value.PublishableKey,
            };
        }
        [HttpGet("CreateCheckoutSession/{userId}/{productName}")]
        public async Task<IActionResult> CreateCheckoutSession([FromRoute] string userId, [FromRoute] string productName)
        {
             ProductObj? productObj=this.options.Value.Products.Where(w => w.ProductName==productName).FirstOrDefault();
             if (productObj==null || productObj.PriceId==null){
                string message=" Unable to find product info for product name "+productName+ " . ";
                _logger.Error(message);
                return BadRequest(new ErrorResponse
                {
                    ErrorMessage = new ErrorMessage
                    {
                        Message = message,
                    }
                });
             }
            var options = new SessionCreateOptions
            {
                SuccessUrl = this.options.Value.Domain + "?success=true&session_id={CHECKOUT_SESSION_ID}&initViewSub=true",
                CancelUrl = this.options.Value.Domain + "?canceled=true",
                Mode = "subscription",
                  
                 LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        Price = productObj.PriceId,
                        Quantity = 1,
                    },
                },
                // AutomaticTax = new SessionAutomaticTaxOptions { Enabled = true },
            };
            var service = new SessionService(this.client);
            try
            {
                var session = await service.CreateAsync(options);
                Response.Headers.Add("Location", session.Url);
                _stripeService.SessionList.Add(session.Id, userId);
                _logger.Info("Success : Added UserID " + userId + " to SessionList with customerId " + session.CustomerId + " Add got sessionId " + session.Id);
                return new StatusCodeResult(303);
            }
            catch (StripeException e)
            {
                _logger.Error(" Stripe Error . Error was : "+e.StripeError.Message);
                return BadRequest(new ErrorResponse
                {
                    ErrorMessage = new ErrorMessage
                    {
                        Message = e.StripeError.Message,
                    }
                });
            }
        }
        [HttpGet("checkout-session")]
        public async Task<IActionResult> CheckoutSession(string sessionId)
        {
            var service = new SessionService(this.client);
            var session = await service.GetAsync(sessionId);
            return Ok(session);
        }
        [HttpGet("customer-portal/{customerId}")]
        public async Task<IActionResult> CustomerPortal([FromRoute] string customerId)
        {
             if (customerId==null || customerId=="" || customerId.Length>100){
                string message="Customer Portal Request : Malformed CustomerID .";
                _logger.Error(message);
                return BadRequest(new ErrorResponse
                {
                    ErrorMessage = new ErrorMessage
                    {
                        Message = message,
                    }
                });
             }
             /*string sessionId = Request.Form["session_Id"];
            string customerId = Request.Form["customer_Id"];
            if (customerId == null)
            {
                var checkoutService = new SessionService(this.client);
                var checkoutSession = await checkoutService.GetAsync(sessionId);
                customerId = checkoutSession.CustomerId;
            }
            */
            var returnUrl = this.options.Value.Domain;
            var options = new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = customerId,
                ReturnUrl = returnUrl,
            };
            var service = new Stripe.BillingPortal.SessionService(this.client);
            var session = await service.CreateAsync(options);
            Response.Headers.Add("Location", session.Url);
            return new StatusCodeResult(303);
        }
        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            Event stripeEvent;
            try
            {
                stripeEvent = EventUtility.ConstructEvent(
                    json,
                    Request.Headers["Stripe-Signature"],
                    this.options.Value.WebhookSecret
                );
                Console.WriteLine($"Webhook notification with type: {stripeEvent.Type} found for {stripeEvent.Id}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Something failed {e}");
                return BadRequest();
            }
            if (stripeEvent.Type == "checkout.session.completed")
            {
                var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
                _stripeService.CreateUserSubscription(session);
                _logger.Info("SUCCESS : Got userId " + _stripeService.SessionList[session.Id]);
                Console.WriteLine($"Session ID: {session.Id}");
                // Take some action based on session.
            }
            if (stripeEvent.Type == Events.CustomerSubscriptionUpdated)
            {
                var session = stripeEvent.Data.Object as Subscription;
                _stripeService.UpdateUserSubscription(session);
                Console.WriteLine($"Updating customer subcription for customerId: {session.Customer}");
                // Take some action based on session.
            }
            return Ok();
        }
    }
}
