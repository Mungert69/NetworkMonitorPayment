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
        private List<PaymentTransaction> _paymentTransactions = new List<PaymentTransaction>();
        private RabbitListener _rabbitRepo;
        private ILogger _logger;
        public readonly IOptions<PaymentOptions> options;
        public StripeService(INetLoggerFactory loggerFactory, IOptions<PaymentOptions> options)
        {

            this.options = options;
            _logger = loggerFactory.GetLogger("StripeService");
            
            try{
                FileRepo.CheckFileExists("PaymentTransactions", _logger);
                _paymentTransactions=FileRepo.GetStateStringJsonZ<List<PaymentTransaction>>("PaymentTranactions");
                int count=0;
                if (_paymentTransactions!=null){
                    count=_paymentTransactions.Count;
                }
                _logger.Info(" Loaded "+count+" PaymentTranctions from State.");
            }
            catch(Exception e){
                _paymentTransactions=new List<PaymentTransaction>();
                _logger.Error(" Failed to load PaymentTransactions from State. Error was : "+e.ToString());
            }
            _rabbitRepo = new RabbitListener(_logger, this, this.options.Value.InstanceName, this.options.Value.HostName);
        }
        public Dictionary<string, string> SessionList { get => _sessionList; set => _sessionList = value; }

        private ResultObj SaveTransactions()
        {
            var result = new ResultObj();

            try
            {
                FileRepo.SaveStateJsonZ<List<PaymentTransaction>>("PaymentTransactions", _paymentTransactions);
                result.Message = " Save Transactions";
                result.Success = true;
            }
            catch (Exception e)
            {
                result.Message = " Failed to Save Transactions. Error was : " + e.ToString();
                result.Success = false;
                _logger.Error(result.Message);
            }
            return result;
        }
        public ResultObj UpdateUserSubscription(Subscription session)
        {
            var result = new ResultObj();
            var userInfo = new UserInfo();
            var paymentTransaction = new PaymentTransaction(){
                    Id = _paymentTransactions.Max(m => m.Id),
                    EventDate = DateTime.UtcNow,
                    UserInfo = userInfo,
                    IsUpdate = true,
                    IsComplete = false,
                    Result = result
                };
            result.Message = "SERVICE : UpdateUserSubscription : ";
            try
            {
                SubscriptionItem item = session.Items.FirstOrDefault();
                if (item != null)
                {
                    ProductObj paymentObj = this.options.Value.Products.Where(w => w.PriceId == item.Price.Id).FirstOrDefault();
                    if (paymentObj != null)
                    {
                        userInfo.AccountType = paymentObj.ProductName;
                        userInfo.HostLimit = paymentObj.HostLimit;
                        userInfo.CancelAt = session.CancelAt;
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
                    paymentTransaction.UserInfo=userInfo;
                    PublishRepo.UpdateUserSubscription(_logger, _rabbitRepo, paymentTransaction);
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
            finally
            {
                paymentTransaction.UserInfo=userInfo;
                paymentTransaction.Result=result;
                _paymentTransactions.Add(paymentTransaction);
                result.Message+=SaveTransactions();
            }
            return result;
        }
        public ResultObj CreateUserSubscription(Stripe.Checkout.Session session)
        {
            var result = new ResultObj();
            var userInfo = new UserInfo();
            var  paymentTransaction=new PaymentTransaction()
                {
                    Id = _paymentTransactions.Max(m => m.Id),
                    EventDate = DateTime.UtcNow,
                    UserInfo = userInfo,
                    IsUpdate = false,
                    IsComplete = false,
                    Result = result
                };
            result.Message = "SERVICE : CreateUserSubscription : ";
            try
            {

                userInfo.UserID = _sessionList[session.Id];
                userInfo.CustomerId = session.CustomerId;

                if (userInfo.UserID != null)
                {
                    paymentTransaction.UserInfo=userInfo;
                    PublishRepo.CreateUserSubscription(_logger, _rabbitRepo, paymentTransaction);
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
            finally
            {
                paymentTransaction.UserInfo=userInfo;
                paymentTransaction.Result=result;
                _paymentTransactions.Add(paymentTransaction);
               
                result.Message+=SaveTransactions();
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