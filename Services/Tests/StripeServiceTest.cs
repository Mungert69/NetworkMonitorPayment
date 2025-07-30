using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetworkMonitor.Payment.Services;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.Factory;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Utils.Helpers;

public class StripeServiceTest
{
    private readonly Mock<ILogger<StripeService>> _loggerMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ISystemParamsHelper> _systemParamsHelperMock;
    private readonly Mock<IOptions<PaymentOptions>> _optionsMock;
    private readonly Mock<IFileRepo> _fileRepoMock;
    private readonly CancellationTokenSource _cts;
    private readonly PaymentOptions _paymentOptions;

    public StripeServiceTest()
    {
        _loggerMock = new Mock<ILogger<StripeService>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();
        _systemParamsHelperMock = new Mock<ISystemParamsHelper>();
        _optionsMock = new Mock<IOptions<PaymentOptions>>();
        _fileRepoMock = new Mock<IFileRepo>();
        _cts = new CancellationTokenSource();

        _paymentOptions = new PaymentOptions
        {
            StripeProducts = new List<ProductObj>
            {
                new ProductObj { ProductName = "Pro", PriceId = "price_123", HostLimit = 10 },
                new ProductObj { ProductName = "Free", PriceId = "price_free", HostLimit = 1 }
            },
            SystemUrls = new List<SystemUrl> { new SystemUrl { ExternalUrl = "http://localhost" } },
            LocalSystemUrl = new SystemUrl { ExternalUrl = "http://localhost" }
        };
        _optionsMock.Setup(x => x.Value).Returns(_paymentOptions);
        // Do NOT mock CreateLogger<T>() -- Moq can't do it. Just pass the mock factory, but don't set up CreateLogger.
    }

    private StripeService CreateService()
    {
        return new StripeService(
            _loggerMock.Object,
            _loggerFactoryMock.Object,
            _systemParamsHelperMock.Object,
            _optionsMock.Object,
            _cts,
            _fileRepoMock.Object
        );
    }

    [Fact]
    public async Task RegisterUser_AddsNewUser()
    {
        // Arrange
        var service = CreateService();
        var user = new RegisteredUser { UserId = "user1", UserEmail = "test@example.com", ExternalUrl = "http://localhost" };
        _fileRepoMock.Setup(f => f.SaveStateJsonAsync<List<RegisteredUser>>("RegisteredUsers", It.IsAny<List<RegisteredUser>>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.RegisterUser(user);

        // Assert
        Assert.Contains(service.RegisteredUsers, u => u.UserId == "user1");
        Assert.Contains("Success", result.Message);
    }

    [Fact]
    public async Task UpdateCustomerID_UpdatesExistingUser()
    {
        // Arrange
        var service = CreateService();
        var user = new RegisteredUser { UserId = "user1", UserEmail = "test@example.com", CustomerId = "cus_123", ExternalUrl = "http://localhost" };
        service.RegisteredUsers.Add(user);
        _fileRepoMock.Setup(f => f.SaveStateJsonAsync<List<RegisteredUser>>("RegisteredUsers", It.IsAny<List<RegisteredUser>>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.UpdateCustomerID(new RegisteredUser { UserId = "user1", UserEmail = "test@example.com", CustomerId = "cus_456" });

        // Assert
        Assert.Equal("cus_456", user.CustomerId);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ProcessPaymentLink_ReturnsTResultObj()
    {
        // Arrange
        var service = CreateService();

        // Act
        var tResult = await service.ProcessPaymentLink("test@example.com", "price_123", "evt_1");

        // Assert
        Assert.True(tResult is TResultObj<string>);
    }

    [Fact]
    public async Task DeleteCustomerID_UserNotFound_ReturnsError()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.DeleteCustomerID(new RegisteredUser { UserId = "notfound", UserEmail = "notfound@example.com" }, "evt_1");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Error", result.Message);
    }

    [Fact]
    public async Task WakeUp_PublishesEvent_ReturnsSuccess()
    {
        // Arrange
        var service = CreateService();
        // PublishRepo.PaymentReadyAsync is static, so we can't mock it easily.
        // This test will just check that the method returns Success = true if no exception is thrown.
        // You may want to refactor PublishRepo for better testability.

        // Act
        var result = await service.WakeUp();

        // Assert
        Assert.True(result.Success);
        Assert.Contains("WakeUp", result.Message);
    }

    [Fact]
    public async Task PaymentCheck_NoTransactions_ReturnsSuccess()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.PaymentCheck();

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Payment Transaction Queue Checked", result.Message);
    }

    [Fact]
    public async Task PaymentComplete_TransactionNotFound_ReturnsError()
    {
        // Arrange
        var service = CreateService();
        var paymentTransaction = new PaymentTransaction { Id = 999, Result = new TResultObj<string> { Success = true } };

        // Act
        var result = await service.PaymentComplete(paymentTransaction);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to Find PaymentTransaction", result.Message);
    }

    [Fact]
    public async Task PingInfosComplete_TransactionNotFound_ReturnsError()
    {
        // Arrange
        var service = CreateService();
        var paymentTransaction = new PaymentTransaction { Id = 999, Result = new TResultObj<string> { Success = true } };

        // Act
        var result = await service.PingInfosComplete(paymentTransaction);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to Find PaymentTransaction", result.Message);
    }
}