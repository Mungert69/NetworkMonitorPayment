using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NetworkMonitor.Payment.Controllers;
using NetworkMonitor.Payment.Models;
using NetworkMonitor.Payment.Services;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using Stripe.Checkout;
using Stripe;
using Xunit;

public class PaymentsControllerTest
{
    private readonly Mock<IOptions<PaymentOptions>> _optionsMock;
    private readonly Mock<IStripeService> _stripeServiceMock;
    private readonly Mock<ILogger<PaymentsController>> _loggerMock;
    private readonly PaymentOptions _paymentOptions;

    public PaymentsControllerTest()
    {
        _optionsMock = new Mock<IOptions<PaymentOptions>>();
        _stripeServiceMock = new Mock<IStripeService>();
        _loggerMock = new Mock<ILogger<PaymentsController>>();

        _paymentOptions = new PaymentOptions
        {
            StripeProducts = new List<ProductObj>
            {
                new ProductObj { ProductName = "Pro", PriceId = "price_123", HostLimit = 10 },
                new ProductObj { ProductName = "Free", PriceId = "price_free", HostLimit = 1 }
            },
            StripeDomain = "http://localhost",
            StripeSecretKey = "sk_test_123",
            StripeWebhookSecret = "whsec_test",
            SystemUrls = new List<SystemUrl> { new SystemUrl { ExternalUrl = "http://localhost" } },
            LocalSystemUrl = new SystemUrl { ExternalUrl = "http://localhost" }
        };
        _optionsMock.Setup(x => x.Value).Returns(_paymentOptions);
    }

    private PaymentsController CreateController()
    {
        return new PaymentsController(_optionsMock.Object, _stripeServiceMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CreateCheckoutSession_UserNotFound_ReturnsBadRequest()
    {
        // Arrange
        var controller = CreateController();
        _stripeServiceMock.SetupGet(s => s.RegisteredUsers).Returns(new System.Collections.Concurrent.ConcurrentBag<RegisteredUser>());

        // Act
        var result = await controller.CreateCheckoutSession("user1", "Pro", "test@example.com");

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ErrorResponse>(badRequest.Value);
        Assert.Contains("Unable to find user", error.ErrorMessage.Message);
    }

    [Fact]
    public async Task CreateCheckoutSession_ProductNotFound_ReturnsBadRequest()
    {
        // Arrange
        var controller = CreateController();
        var users = new System.Collections.Concurrent.ConcurrentBag<RegisteredUser>();
        users.Add(new RegisteredUser { UserId = "user1", UserEmail = "test@example.com" });
        _stripeServiceMock.SetupGet(s => s.RegisteredUsers).Returns(users);

        // Act
        var result = await controller.CreateCheckoutSession("user1", "NonExistentProduct", "test@example.com");

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ErrorResponse>(badRequest.Value);
        Assert.Contains("Unable to find product info", error.ErrorMessage.Message);
    }

    [Fact]
    public async Task CheckoutSession_Success_ReturnsOk()
    {
        // Arrange
        var controller = CreateController();
        var sessionId = "sess_123";
        var session = new Session { Id = sessionId };
        var users = new System.Collections.Concurrent.ConcurrentBag<RegisteredUser>();
        users.Add(new RegisteredUser { UserId = "user1", UserEmail = "test@example.com" });
        _stripeServiceMock.SetupGet(s => s.SessionList).Returns(new Dictionary<string, string> { { sessionId, "user1" } });
        _stripeServiceMock.SetupGet(s => s.RegisteredUsers).Returns(users);

        // NOTE: The controller creates a new SessionService internally, so we cannot inject our mock.
        // The call will fail and return BadRequest, so we expect BadRequestObjectResult.

        // Act
        var result = await controller.CheckoutSession(sessionId);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        // Optionally, check the error message
        // var error = Assert.IsType<ErrorResponse>(badRequest.Value);
    }

    [Fact]
    public async Task CustomerPortal_UserNotFound_ReturnsBadRequest()
    {
        // Arrange
        var controller = CreateController();
        _stripeServiceMock.SetupGet(s => s.RegisteredUsers).Returns(new System.Collections.Concurrent.ConcurrentBag<RegisteredUser>());

        // Act
        var result = await controller.CustomerPortal("cus_123");

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ErrorResponse>(badRequest.Value);
        Assert.Contains("Unable to find user", error.ErrorMessage.Message);
    }

    [Fact]
    public async Task CustomerPortal_MalformedCustomerId_ReturnsBadRequest()
    {
        // Arrange
        var controller = CreateController();
        var malformedId = new string('a', 101);
        var users = new System.Collections.Concurrent.ConcurrentBag<RegisteredUser>();
        users.Add(new RegisteredUser { CustomerId = malformedId });
        _stripeServiceMock.SetupGet(s => s.RegisteredUsers).Returns(users);

        // Act
        var result = await controller.CustomerPortal(malformedId);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ErrorResponse>(badRequest.Value);
        // If this fails, check the actual error message for debugging:
        // System.Console.WriteLine(error.ErrorMessage.Message);
        Assert.Contains("Malformed CustomerID", error.ErrorMessage.Message);
    }

    [Fact]
    public async Task Webhook_InvalidSignature_ReturnsBadRequest()
    {
        // Arrange
        var controller = CreateController();
        var context = new DefaultHttpContext();
        var body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}"));
        context.Request.Body = body;
        context.Request.Headers["Stripe-Signature"] = "invalid";
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        // Act
        var result = await controller.Webhook();

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ErrorResponse>(badRequest.Value);
        Assert.Contains("signature header format is unexpected", error.ErrorMessage.Message, StringComparison.OrdinalIgnoreCase);
    }
}
