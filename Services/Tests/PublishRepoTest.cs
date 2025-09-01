using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;

public class PublishRepoTest
{
    private readonly Mock<ILogger> _loggerMock;
    private readonly Mock<IRabbitRepo> _rabbitRepoMock;
    private readonly List<IRabbitRepo> _rabbitRepos;

    public PublishRepoTest()
    {
        _loggerMock = new Mock<ILogger>();
        _rabbitRepoMock = new Mock<IRabbitRepo>();
        _rabbitRepoMock.SetupGet(r => r.SystemUrl).Returns(new SystemUrl { ExternalUrl = "http://localhost" });
        _rabbitRepos = new List<IRabbitRepo> { _rabbitRepoMock.Object };
    }

    [Fact]
    public async Task GetProductsAsync_PublishesToAllRepos()
    {
        var updateProductObj = new UpdateProductObj();
        await PublishRepo.GetProductsAsync(_loggerMock.Object, _rabbitRepos, updateProductObj);

        _rabbitRepoMock.Verify(r => r.PublishAsync("getProducts", null,""), Times.Once);
    }

    [Fact]
    public async Task PaymentReadyAsync_PublishesToAllRepos()
    {
        await PublishRepo.PaymentReadyAsync(_loggerMock.Object, _rabbitRepos, true);

        _rabbitRepoMock.Verify(r => r.PublishAsync<PaymentServiceInitObj>(
            It.Is<string>(s => s == "paymentServiceReady"),
            It.IsAny<PaymentServiceInitObj>(),
            ""), Times.Once);
    }

    [Fact]
    public async Task UpdateUserSubscriptionAsync_FindsRepoAndPublishes_ReturnsTrue()
    {
        var paymentTransaction = new PaymentTransaction { ExternalUrl = "http://localhost", UserInfo = new UserInfo { CustomerId = "cus_123" } };
        var result = await PublishRepo.UpdateUserSubscriptionAsync(_loggerMock.Object, _rabbitRepos, paymentTransaction);

        Assert.True(result);
        _rabbitRepoMock.Verify(r => r.PublishAsync<PaymentTransaction>(
            "updateUserSubscription", paymentTransaction, ""), Times.Once);
    }

    [Fact]
    public async Task UpdateUserSubscriptionAsync_RepoNotFound_ReturnsFalse()
    {
        var paymentTransaction = new PaymentTransaction { ExternalUrl = "http://notfound", UserInfo = new UserInfo { CustomerId = "cus_123" } };
        var result = await PublishRepo.UpdateUserSubscriptionAsync(_loggerMock.Object, _rabbitRepos, paymentTransaction);

        Assert.False(result);
        _rabbitRepoMock.Verify(r => r.PublishAsync<PaymentTransaction>(
            "updateUserSubscription", paymentTransaction, ""), Times.Never);
    }

    [Fact]
    public async Task BoostTokenForUserAsync_FindsRepoAndPublishes_ReturnsTrue()
    {
        var paymentTransaction = new PaymentTransaction { ExternalUrl = "http://localhost", UserInfo = new UserInfo { CustomerId = "cus_123" } };
        var result = await PublishRepo.BoostTokenForUserAsync(_loggerMock.Object, _rabbitRepos, paymentTransaction);

        Assert.True(result);
        _rabbitRepoMock.Verify(r => r.PublishAsync<PaymentTransaction>(
            "boostTokenForUser", paymentTransaction, ""), Times.Once);
    }

    [Fact]
    public async Task BoostTokenForUserAsync_RepoNotFound_ReturnsFalse()
    {
        var paymentTransaction = new PaymentTransaction { ExternalUrl = "http://notfound", UserInfo = new UserInfo { CustomerId = "cus_123" } };
        var result = await PublishRepo.BoostTokenForUserAsync(_loggerMock.Object, _rabbitRepos, paymentTransaction);

        Assert.False(result);
        _rabbitRepoMock.Verify(r => r.PublishAsync<PaymentTransaction>(
            "boostTokenForUser", paymentTransaction, ""), Times.Never);
    }

    [Fact]
    public async Task UpdateUserPingInfosAsync_FindsRepoAndPublishes_ReturnsTrue()
    {
        var paymentTransaction = new PaymentTransaction { ExternalUrl = "http://localhost", UserInfo = new UserInfo { CustomerId = "cus_123" } };
        var result = await PublishRepo.UpdateUserPingInfosAsync(_loggerMock.Object, _rabbitRepos, paymentTransaction);

        Assert.True(result);
        _rabbitRepoMock.Verify(r => r.PublishAsync<PaymentTransaction>(
            "updateUserPingInfos", paymentTransaction, ""), Times.Once);
    }

    [Fact]
    public async Task UpdateUserPingInfosAsync_RepoNotFound_ReturnsFalse()
    {
        var paymentTransaction = new PaymentTransaction { ExternalUrl = "http://notfound", UserInfo = new UserInfo { CustomerId = "cus_123" } };
        var result = await PublishRepo.UpdateUserPingInfosAsync(_loggerMock.Object, _rabbitRepos, paymentTransaction);

        Assert.False(result);
        _rabbitRepoMock.Verify(r => r.PublishAsync<PaymentTransaction>(
            "updateUserPingInfos", paymentTransaction, ""), Times.Never);
    }

    [Fact]
    public async Task UpdateUserCustomerIdAsync_FindsRepoAndPublishes_ReturnsTrue()
    {
        var paymentTransaction = new PaymentTransaction { ExternalUrl = "http://localhost", UserInfo = new UserInfo { UserID = "user1" } };
        var result = await PublishRepo.UpdateUserCustomerIdAsync(_loggerMock.Object, _rabbitRepos, paymentTransaction);

        Assert.True(result);
        _rabbitRepoMock.Verify(r => r.PublishAsync<PaymentTransaction>(
            "updateUserCustomerId", paymentTransaction, ""), Times.Once);
    }

    [Fact]
    public async Task UpdateUserCustomerIdAsync_RepoNotFound_ReturnsFalse()
    {
        var paymentTransaction = new PaymentTransaction { ExternalUrl = "http://notfound", UserInfo = new UserInfo { UserID = "user1" } };
        var result = await PublishRepo.UpdateUserCustomerIdAsync(_loggerMock.Object, _rabbitRepos, paymentTransaction);

        Assert.False(result);
        _rabbitRepoMock.Verify(r => r.PublishAsync<PaymentTransaction>(
            "updateUserCustomerId", paymentTransaction, ""), Times.Never);
    }
}
