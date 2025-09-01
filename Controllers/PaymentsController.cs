using System;
using System.IO;
using System.Threading.Tasks;
using Stripe;
using System.Linq; // For LINQ methods like Where, FirstOrDefault
using System.Collections.Generic; // For List and other collections
using Microsoft.AspNetCore.Mvc; // For IActionResult, Controller, HttpGet, etc.
using Microsoft.Extensions.Logging; // For ILogger
using Microsoft.Extensions.Options; // For IOptions
using NetworkMonitor.Payment.Services; // Assuming IStripeService and other custom services are part of this namespace
using NetworkMonitor.Payment.Models; // Assuming models like ProductObj, RegisteredUser, ResultObj, etc.
using NetworkMonitor.Objects;
using Stripe.Checkout;
using NetworkMonitor.Objects.ServiceMessage;
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
            result.Message = " API : CreateCheckoutSesion : ";

            // Look for a Registerd User Object in _stripeService.RegisteredUsers with the userId/
            // If not found, return BadRequest with error message
            if (_stripeService.RegisteredUsers.Where(w => w.UserId == userId).FirstOrDefault() == null)
            {
                result.Message += " Error : Unable to find user with userId " + userId + " .";
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



            ProductObj? productObj = _stripeService.Products.Where(w => w.ProductName == productName).FirstOrDefault();
            if (productObj == null || productObj.PriceId == null)
            {
                result.Message += " Error : Unable to find product info for product name " + productName + " . ";
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
#pragma warning disable ASP0019 // Suggest using IHeaderDictionary.Append or the indexer
                Response.Headers.Add("Location", session.Url);
#pragma warning restore ASP0019 // Suggest using IHeaderDictionary.Append or the indexer
                _stripeService.SessionList.Add(session.Id, userId);
                _logger.LogInformation(" Success : Redirecting to Checkout session for UserID " + userId + " with customerId " + session.CustomerId + " , sessionId " + session.Id + " . ");
                return new StatusCodeResult(303);
            }
            catch (StripeException e)
            {
                _logger.LogError(" Error : Can not create Checkout Sessions . Stripe Error . Error was : " + e.StripeError.Message + " . ");
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
            var result = new ResultObj();
            result.Message = " API : CheckoutSession : ";
            try
            {
                var service = new SessionService(this.client);
                var session = await service.GetAsync(sessionId);
                string? userId = _stripeService.SessionList[sessionId];
                result.Message += $" Success : Returning Checkout Session for userId {userId} sessionId {sessionId}";

                _logger.LogInformation(result.Message);
                return Ok(session);
            }
            catch (Exception e)
            {
                _logger.LogError($" Error : Can not return Checkout Session . Error was : {e.Message}");
                return BadRequest(new ErrorResponse
                {
                    ErrorMessage = new ErrorMessage
                    {
                        Message = e.Message,
                    }
                });
            }

        }
        [HttpGet("customer-portal/{customerId}")]
        public async Task<IActionResult> CustomerPortal([FromRoute] string customerId)
        {
            var result = new ResultObj();
            result.Message = " API : CustomerPortal : ";
            try
            {
                if (_stripeService.RegisteredUsers.Where(w => w.CustomerId == customerId).FirstOrDefault() == null)
                {
                    result.Message += " Error : Unable to find user with customerId " + customerId + " .";
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
                    result.Message += " Error : Customer Portal Request : Malformed CustomerID. ";
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
#pragma warning disable ASP0019 // Suggest using IHeaderDictionary.Append or the indexer
                Response.Headers.Add("Location", session.Url);
#pragma warning restore ASP0019 // Suggest using IHeaderDictionary.Append or the indexer
                _logger.LogInformation($" Success : Redirecting to Billing portal  for customerId {customerId}");

                return new StatusCodeResult(303);

            }
            catch (Exception e)
            {
                result.Message += $" Error : Can not redirect to Billing Portal . Error was : {e.Message}";
                _logger.LogError(result.Message);
                return BadRequest(new ErrorResponse
                {
                    ErrorMessage = new ErrorMessage
                    {
                        Message = result.Message,
                    }
                });
            }
        }
        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook()
        {
            var result = new ResultObj();
            result.Success = false;
            result.Message = " API : Webhook : ";
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            Stripe.Event stripeEvent;
            try
            {
                stripeEvent = EventUtility.ConstructEvent(
                    json,
                    Request.Headers["Stripe-Signature"],
                    this.options.Value.StripeWebhookSecret,
                    throwOnApiVersionMismatch: false
                );
                _logger.LogInformation($" Webhook notification with type: {stripeEvent.Type} found for {stripeEvent.Id}");
            }
            catch (Exception e)
            {
                _logger.LogError(" Error : Constructing StripeEvent . Error was : " + e.Message);
                return BadRequest(new ErrorResponse
                {
                    ErrorMessage = new ErrorMessage
                    {
                        Message = e.Message,
                    }
                });
            }

            if (stripeEvent.Type == EventTypes.CustomerCreated)
            {
                var session = stripeEvent.Data.Object as Stripe.Customer;

                if (session != null)
                {
                    var registeredUser = new RegisteredUser()
                    {
                        UserEmail = session.Email,
                        CustomerId = session.Id,
                    };
                    result = await _stripeService.UpdateCustomerID(registeredUser);
                    _logger.LogInformation($" Created customer for UserId {registeredUser.UserId} .");
                }
                else
                {
                    _logger.LogError("Error : stripeEvent CustomerCreated contains no Session object .");
                }

            }
            if (stripeEvent.Type == EventTypes.CustomerDeleted)
            {
                var session = stripeEvent.Data.Object as Stripe.Customer;

                if (session != null)
                {
                    var registeredUser = new RegisteredUser()
                    {
                        UserEmail = session.Email,
                        CustomerId = session.Id,
                    };
                    result = await _stripeService.DeleteCustomerID(registeredUser, stripeEvent.Id);
                    _logger.LogInformation($" Deleted customer for UserId {registeredUser.UserId} .");
                }
                else
                {
                    _logger.LogError("Error : stripeEvent CustomerCreated contains no Session object .");
                }

            }

            if (stripeEvent.Type == EventTypes.CheckoutSessionCompleted)
            {
                var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
                if (session!=null && !string.IsNullOrEmpty(session.PaymentLinkId))
                {
                    var email = session.CustomerDetails.Email;
                    if (string.IsNullOrEmpty(email))
                    {
                        _logger.LogError("Error : stripeEvent CheckoutSessionCompleted with PaymentLinkId contains no customer Email .");
                    }
                    else
                    {
                        var tResult = await _stripeService.ProcessPaymentLink(email, session.PaymentLinkId, stripeEvent.Id);
                        result.Success = tResult.Success;
                        result.Message += tResult.Message;
                        result.Data = tResult.Data;

                    }

                }
                else
                {
                    if (session != null)
                    {
                        var registeredUser = new RegisteredUser()
                        {
                            UserEmail = session.CustomerEmail,
                            CustomerId = session.CustomerId,
                            UserId = session.ClientReferenceId
                        };
                        result = await _stripeService.UpdateCustomerID(registeredUser);
                        _logger.LogInformation($" Checkout session complete  for UserId {registeredUser.UserId} .");
                    }
                    else
                    {
                        _logger.LogError("Error : stripeEvent CustomerCreated contains no Session object .");
                    }

                }

            }

            if (stripeEvent.Type == EventTypes.CustomerSubscriptionDeleted)
            {
                var session = stripeEvent.Data.Object as Subscription;

                if (session == null || session.CustomerId == null)
                {
                    _logger.LogError("Error : stripeEvent CustomerSubscriptionDeleted contains no CustomerId .");
                }
                else
                {
                    _logger.LogInformation($" Deleting customer subcription for customerId: {session.CustomerId}");
                    var tResult = await _stripeService.DeleteUserSubscription(session.CustomerId, stripeEvent.Id);
                    result.Success = tResult.Success;
                    result.Message += tResult.Message;
                    result.Data = tResult.Data;
                }

            }

            if (stripeEvent.Type == EventTypes.CustomerSubscriptionCreated)
            {
                var session = stripeEvent.Data.Object as Subscription;

                if (session == null || session.CustomerId == null)
                {
                    _logger.LogError("Error : stripeEvent CustomerSubscriptionCreated contains no CustomerId .");
                }
                else
                {
                    _logger.LogInformation($"Creating customer subcription for customerId: {session.Customer}");
                    var tResult = await _stripeService.UpdateUserCustomerId(session.CustomerId, stripeEvent.Id);
                    var items = session.Items;
                    var tsResult = new TResultObj<string>();
                    SubscriptionItem? item = items.FirstOrDefault();
                    if (item != null && item.Price != null && item.Price.Id != null)
                    {

                        _logger.LogInformation($" Updating customer subcription for customerId: {session.CustomerId} Price.Id {item.Price.Id}");

                        tsResult = await _stripeService.UpdateUserSubscription(session.CustomerId, stripeEvent.Id + "_create", item.Price.Id, null);

                    }
                    else
                    {
                        tsResult.Message += " Error : No Items found is session returned from Stripe. Check dashboard for correctly setup Prices. ";
                    }
                    result.Success = tResult.Success && tsResult.Success;
                    result.Message += tResult.Message + tsResult.Message;
                    if (tsResult != null) result.Data = tsResult.Data;
                    else result.Data = tResult.Data;
                }

            }
            if (stripeEvent.Type == EventTypes.CustomerSubscriptionUpdated)
            {
                var session = stripeEvent.Data.Object as Subscription;


                if (session == null || session.CustomerId == null)
                {
                    _logger.LogError("Error : stripeEvent CustomerSubscriptionUpdated contains no customerId .");
                }
                else
                {
                    var items = session.Items;
                    SubscriptionItem? item = items.FirstOrDefault();
                    if (item != null && item.Price != null && item.Price.Id != null)
                    {

                        _logger.LogInformation($" Updating customer subcription for customerId: {session.Customer}");

                        var tResult = await _stripeService.UpdateUserSubscription(session.CustomerId, stripeEvent.Id, item.Price.Id, session.CancelAt);
                        result.Success = tResult.Success;
                        result.Message += tResult.Message;
                        result.Data = tResult.Data;
                    }
                    else
                    {
                        result.Message += " Error : No Items found is session returned from Stripe. Check dashboard for correctly setup Prices. ";
                    }

                }

            }
            if (result.Success)
            {
                _logger.LogInformation(result.Message);
                return Ok();
            }

            else
            {
                _logger.LogError($" Error : Webhook failed .  Error was : {result.Message}");

                return BadRequest(new ErrorResponse
                {
                    ErrorMessage = new ErrorMessage
                    {
                        Message = result.Message,
                    }
                });
            }

        }
    }
}
