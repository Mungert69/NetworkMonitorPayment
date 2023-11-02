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
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Objects;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
namespace NetworkMonitor.Payment.Controllers
{
    public class PaymentsController : Controller
    {
        public readonly IOptions<PaymentOptions> options;
        private readonly IStripeClient client;
        private IStripeService _stripeService;
        private ILogger _logger;
        public PaymentsController(IOptions<PaymentOptions> options, IStripeService stripeService, ILogger<PaymentsController> logger)
        {
            _logger = logger;
            _stripeService = stripeService;
            this.options = options;
            this.client = new StripeClient(this.options.Value.StripeSecretKey);
        }

        [HttpGet("CreateCheckoutSession/{userId}/{productName}/{email}")]
        public async Task<IActionResult> CreateCheckoutSession([FromRoute] string userId, [FromRoute] string productName, [FromRoute] string email)
        {
            var result = new ResultObj();
            result.Message = " API : CreateCheckoutSesion ";

            // Look for a Registerd User Object in _stripeService.RegisteredUsers with the userId/
            // If not found, return BadRequest with error message
            if (_stripeService.RegisteredUsers.Where(w => w.UserId == userId).FirstOrDefault() == null)
            {
                result.Message += " Unable to find user with userId " + userId + " .";
                result.Success = false;
                _logger.LogError(result.Message);
                return BadRequest(new ErrorResponse
                {
                    ErrorMessage = new ErrorMessage
                    {
                        Message = result.Message,
                    }
                });
            }



            ProductObj? productObj = this.options.Value.StripeProducts.Where(w => w.ProductName == productName).FirstOrDefault();
            if (productObj == null || productObj.PriceId == null)
            {
                result.Message += " Unable to find product info for product name " + productName + " . ";
                _logger.LogError(result.Message);
                return BadRequest(new ErrorResponse
                {
                    ErrorMessage = new ErrorMessage
                    {
                        Message = result.Message,
                    }
                });
            }
            var options = new SessionCreateOptions
            {
                SuccessUrl = this.options.Value.StripeDomain + "?success=true&session_id={CHECKOUT_SESSION_ID}&initViewSub=true",
                CancelUrl = this.options.Value.StripeDomain + "?canceled=true",
                Mode = "subscription",
                CustomerEmail = email,
                ClientReferenceId = userId,
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
                _logger.LogInformation("Success : Added UserID " + userId + " to SessionList with customerId " + session.CustomerId + " Add got sessionId " + session.Id);
                return new StatusCodeResult(303);
            }
            catch (StripeException e)
            {
                _logger.LogError(" Stripe Error . Error was : " + e.StripeError.Message);
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
            var result = new ResultObj();
            result.Message = " API : CustomerPortal ";

            if (_stripeService.RegisteredUsers.Where(w => w.CustomerId == customerId).FirstOrDefault() == null)
            {
                result.Message += " Unable to find user with customerId " + customerId + " .";
                result.Success = false;
                _logger.LogError(result.Message);
                return BadRequest(new ErrorResponse
                {
                    ErrorMessage = new ErrorMessage
                    {
                        Message = result.Message,
                    }
                });
            }
            if (customerId == null || customerId == "" || customerId.Length > 100)
            {
                result.Message += " Customer Portal Request : Malformed CustomerID.";
                _logger.LogError(result.Message);
                return BadRequest(new ErrorResponse
                {
                    ErrorMessage = new ErrorMessage
                    {
                        Message = result.Message,
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
            var returnUrl = this.options.Value.StripeDomain;
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
                    this.options.Value.StripeWebhookSecret
                );
                Console.WriteLine($"Webhook notification with type: {stripeEvent.Type} found for {stripeEvent.Id}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Something failed {e}");
                return BadRequest();
            }
            if (stripeEvent.Type == Events.CustomerCreated)
            {
                var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
                if (session != null)
                {
                    var registeredUser=new RegisteredUser(){
                        UserEmail=session.CustomerEmail,
                        CustomerId=session.CustomerId,
                        UserId=session.ClientReferenceId
                    };
                    await _stripeService.UpdateCustomerID(registeredUser);
                    _logger.LogInformation($" Created customer  for UserId {_stripeService.SessionList[session.Id]} .");
                }
                else
                {
                    _logger.LogError("Error : stripeEvent CustomerCreated contains no Session object .");
                }
            }
            if (stripeEvent.Type == Events.CheckoutSessionCompleted)
            {
                var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
                if (session != null )
                {
                    var registeredUser=new RegisteredUser(){
                        UserEmail=session.CustomerEmail,
                        CustomerId=session.CustomerId,
                        UserId=session.ClientReferenceId
                    };
                    await _stripeService.UpdateCustomerID(registeredUser);
                    _logger.LogInformation($" Created customer  for UserId {_stripeService.SessionList[session.Id]} .");
                }
                else
                {
                    _logger.LogError("Error : stripeEvent CustomerCreated contains no Session object .");
                }
            }
            if (stripeEvent.Type == Events.CustomerSubscriptionCreated)
            {
                var session = stripeEvent.Data.Object as Subscription;
                var result = new ResultObj();
                result.Success = false;
                if (session == null || session.CustomerId == null)
                {
                    _logger.LogError("Error : stripeEvent CustomerSubscriptionCreated contains CustomerId .");
                }
                else
                {
                    _logger.LogInformation($"Creating customer subcription for customerId: {session.Customer}");
                    result = await _stripeService.UpdateUserCustomerId(session.CustomerId);

                }
                if (result.Success)
                {
                    return Ok();
                }
                else
                {
                    return BadRequest(new ErrorResponse
                    {
                        ErrorMessage = new ErrorMessage
                        {
                            Message = result.Message,
                        }
                    });
                }
            }
            if (stripeEvent.Type == Events.CustomerSubscriptionUpdated)
            {
                var session = stripeEvent.Data.Object as Subscription;
                var result = new ResultObj();
                result.Success = false;
                if (session == null || session.CustomerId == null)
                {
                    _logger.LogError("Error : stripeEvent CustomerSubscriptionUpdated contains no customerId .");
                }
                else
                {
                    _logger.LogInformation($"Updating customer subcription for customerId: {session.Customer}");

                    result = await _stripeService.UpdateUserSubscription(session);

                }
                if (result.Success)
                {
                    return Ok();
                }
                else
                {
                    return BadRequest(new ErrorResponse
                    {
                        ErrorMessage = new ErrorMessage
                        {
                            Message = result.Message,
                        }
                    });
                }
            }
            return Ok();
        }
    }
}
