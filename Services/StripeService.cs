using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.Factory;
using NetworkMonitor.Payment.Models;
using Microsoft.Extensions.Options;
using MetroLog;
using Stripe;
using Stripe.Checkout;
namespace NetworkMonitor.Payment.Services
{
    public interface IStripeService
    {
        Dictionary<string, string> SessionList { get; set; }
        ResultObj WakeUp();
        ResultObj UpdateUserSubscription(Subscription session);
        ResultObj CreateUserSubscription(Stripe.Checkout.Session session);
    }
    public class StripeService : IStripeService
    {
        private Dictionary<string, string> _sessionList = new Dictionary<string, string>();
        private RabbitListener _rabbitRepo;
        private ILogger _logger;
        public readonly IOptions<PaymentOptions> options;
        public StripeService(INetLoggerFactory loggerFactory, IOptions<PaymentOptions> options)
        {
            this.options = options;
            _logger = loggerFactory.GetLogger("StripeService");
            _rabbitRepo = new RabbitListener(_logger, this, this.options.Value.InstanceName, this.options.Value.HostName);
        }
        public Dictionary<string, string> SessionList { get => _sessionList; set => _sessionList = value; }
        public ResultObj UpdateUserSubscription(Subscription session)
        {
            var result = new ResultObj();
            result.Message = "SERVICE : UpdateUserSubscription : ";
            try
            {
                var userInfo = new UserInfo();
                SubscriptionItem item = session.Items.FirstOrDefault();
                if (item != null)
                {
                    ProductObj paymentObj = this.options.Value.Products.Where(w => w.PriceId == item.Price.Id).FirstOrDefault();
                    if (paymentObj != null)
                    {
                        userInfo.AccountType = paymentObj.ProductName;
                        userInfo.HostLimit = paymentObj.HostLimit;
                        userInfo.CancelAt=session.CancelAt;
                        result.Message += " Success : Changed CustomerID " + session.CustomerId + " Subsciption Product to " + paymentObj.ProductName;
                    }
                    else
                    {
                        result.Message += " Error : Failed to find Product with PriceID " + item.Price.Id;
                        _logger.Error(" Failed to find Product with PriceID " + item.Price.Id);
                    }
                }
                else
                {
                    result.Message += " Error : Subcription Items contains no Subscription for CustomerID " + session.CustomerId;
                    _logger.Error(" Subcription Items contains no Subscription for CustomerID " + session.CustomerId);
                }
                userInfo.CustomerId = session.CustomerId;
                if (userInfo.CustomerId != null)
                {
                    PublishRepo.UpdateUserSubscription(_logger, _rabbitRepo, userInfo);
                    result.Message += " Success : Published event UpdateUserSubscription";
                    result.Success = true;
                }
                else
                {
                    result.Message += " Error : failed update customer with sessionId = " + session.Id;
                    result.Success = false;
                }
                _logger.Info(result.Message);
            }
            catch (Exception e)
            {
                result.Message += "Error : failed publish UpdateUserSubscription . Error was : " + e.Message;
                _logger.Error("Error : failed publish UpdateUserSubscription . Error was : " + e.ToString());
                result.Success = false;
            }
            return result;
        }
        public ResultObj CreateUserSubscription(Stripe.Checkout.Session session)
        {
            var result = new ResultObj();
            result.Message = "SERVICE : CreateUserSubscription : ";
            try
            {
                var userInfo = new UserInfo();
                userInfo.UserID = _sessionList[session.Id];
                userInfo.AccountType = "full";
                userInfo.HostLimit = 100;
                userInfo.CustomerId = session.CustomerId;
                if (userInfo.UserID != null)
                {
                    PublishRepo.CreateUserSubscription(_logger, _rabbitRepo, userInfo);
                    result.Message += "Success : Published event UpdateUserSubscription";
                    result.Success = true;
                }
                else
                {
                    result.Message += "Error : failed to find user with sessionId = " + session.Id;
                    result.Success = false;
                }
                _logger.Info(result.Message);
            }
            catch (Exception e)
            {
                result.Message += "Error : failed publish UpdateUserSubscription . Error was : " + e.Message;
                _logger.Error("Error : failed publish UpdateUserSubscription . Error was : " + e.ToString());
                result.Success = false;
            }
            return result;
        }
        public ResultObj WakeUp()
        {
            var result = new ResultObj();
            return result;
        }
    }
}