using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.Factory;
using Microsoft.Extensions.Options;
using MetroLog;

namespace NetworkMonitor.Payment.Services
{
    public interface IStripeService
    {
        Dictionary<string, string> SessionList { get; set; }
        ResultObj WakeUp();
        ResultObj UpdateUserSubscription(Stripe.Checkout.Session session);
    }

    public class StripeService : IStripeService
    {
        private Dictionary<string, string> _sessionList = new Dictionary<string, string>();
        private RabbitListener _rabbitRepo;
        private ILogger _logger;

        public StripeService(INetLoggerFactory loggerFactory, IOptions<PaymentOptions> options)
        {
            _logger = loggerFactory.GetLogger("StripeService");
            _rabbitRepo = new RabbitListener(_logger, this, options.Value.InstanceName, options.Value.HostName);

        }

        public Dictionary<string, string> SessionList { get => _sessionList; set => _sessionList = value; }

        public ResultObj UpdateUserSubscription(Stripe.Checkout.Session session)
        {
            var result = new ResultObj();
            result.Message = "SERVICE : UpdateUserSubscription : ";
           
            try
            {
                 var userInfo = new UserInfo();
                userInfo.UserID = _sessionList[session.Id];
                userInfo.AccountType="full";
                userInfo.CustomerId=session.CustomerId;
                if (userInfo.UserID != null)
                {
                    PublishRepo.UpdateUserSubscription(_logger, _rabbitRepo, userInfo);
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