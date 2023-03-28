using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.Factory;
using NetworkMonitor.Objects.ServiceMessage;
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
        ResultObj PaymentCheck();
        ResultObj PaymentComplete(PaymentTransaction paymentTransaction);
        ResultObj UpdateUserSubscription(Subscription session);
        ResultObj CreateUserSubscription(Stripe.Checkout.Session session);
        Task Init();
    }
    public class StripeService : IStripeService
    {
        CancellationToken _token;
        private Dictionary<string, string> _sessionList = new Dictionary<string, string>();
        private List<PaymentTransaction> _paymentTransactions = new List<PaymentTransaction>();
        private RabbitListener _rabbitRepo;
        private ILogger _logger;
        public readonly IOptions<PaymentOptions> options;
        public StripeService(INetLoggerFactory loggerFactory, IOptions<PaymentOptions> options, CancellationTokenSource cancellationTokenSource)
        {
            _token=cancellationTokenSource.Token;
            this.options = options;
            _logger = loggerFactory.GetLogger("StripeService");
        }
        public Task Init()
        {
            return Task.Run(() =>
               {
                   var result = new ResultObj();
                   result.Message = " Init Stripe Service : ";
                   try
                   {
                       _token.Register(() => this.Shutdown());
                       FileRepo.CheckFileExists("PaymentTransactions", _logger);
                       _paymentTransactions = FileRepo.GetStateStringJsonZ<List<PaymentTransaction>>("PaymentTransactions");
                       int count = 0;
                       if (_paymentTransactions != null)
                       {
                           count = _paymentTransactions.Count;
                       }
                       result.Message += " Loaded " + count + " PaymentTranctions from State. ";
                       result.Success = true;
                   }
                   catch (Exception e)
                   {
                       result.Success = false;
                       result.Message += " Failed to load PaymentTransactions from State. Error was : " + e.ToString() + " . ";
                   }
                   if (_paymentTransactions == null)
                   {
                       _paymentTransactions = new List<PaymentTransaction>();
                   }
                   if (_paymentTransactions.Count == 0)
                   {
                       _paymentTransactions.Add(new PaymentTransaction());
                   }
                   try
                   {
                       _rabbitRepo = new RabbitListener(_logger,this.options.Value.SystemUrl, this);
                   }
                   catch (Exception e)
                   {
                       result.Message += " Could not setup RabbitListner. Error was : " + e.ToString() + " . ";
                       result.Success = false;
                   }
                   result.Message += " Finished StripeService Init ";
                   result.Success = result.Success && true;
                   if (result.Success)
                       _logger.Info(result.Message);
                   else _logger.Fatal(result.Message);
               });
        }
        public Dictionary<string, string> SessionList { get => _sessionList; set => _sessionList = value; }
        public void Shutdown()
        {
            _logger.Warn(" : SHUTDOWN started :");
            var result = SaveTransactions();
            if (result.Success)
            {
                _logger.Info(result.Message);
            }
            else
            {
                _logger.Error(result.Message);
            }
            _logger.Warn(" : SHUTDOWN completed :");
        }
        public ResultObj PaymentCheck()
        {
            var result = new ResultObj();
            try
            {
                _paymentTransactions.OrderBy(o => o.EventDate).ToList().ForEach(p => {
                    if (p.IsUpdate)  PublishRepo.UpdateUserSubscription(_logger, _rabbitRepo, p);
                    else PublishRepo.CreateUserSubscription(_logger, _rabbitRepo, p);  
                    // TODO : add a delay in here.           
                });
                PublishRepo.PaymentReady(_logger, _rabbitRepo, true);
                result.Message = " Payment Transaction Queue Checked";
                result.Success = true;
                _logger.Info(result.Message);
            }
            catch (Exception e)
            {
                result.Message = " Failed to check Payment Transaction Queue . Error was : " + e.ToString();
                result.Success = false;
                _logger.Error(result.Message);
            }
            return result;
        }
        private ResultObj SaveTransactions()
        {
            var result = new ResultObj();
            try
            {
                FileRepo.SaveStateJsonZ<List<PaymentTransaction>>("PaymentTransactions", _paymentTransactions);
                result.Message = " Save Transactions Completed ";
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
        public ResultObj PaymentComplete(PaymentTransaction paymentTransaction)
        {
            var result = new ResultObj();
            try
            {
                var updatePaymentTransaction = _paymentTransactions.Where(w => w.Id == paymentTransaction.Id).FirstOrDefault();
                if (updatePaymentTransaction != null)
                {
                    _paymentTransactions.Remove(updatePaymentTransaction);
                    result.Message = " Payment Complete ";
                    result.Success = true;
                }
                else
                {
                    result.Message = " Failed find PaymentTransaction with ID " + paymentTransaction.Id;
                    result.Success = true;
                }
            }
            catch (Exception e)
            {
                result.Message = " Failed to Update PaymentTransactions with ID " + paymentTransaction.Id + ". Error was : " + e.ToString();
                result.Success = false;
                _logger.Error(result.Message);
            }
            return result;
        }
        public ResultObj UpdateUserSubscription(Subscription session)
        {
            var result = new ResultObj();
            var userInfo = new UserInfo();
            var paymentTransaction = new PaymentTransaction()
            {
                Id = _paymentTransactions.Max(m => m.Id) + 1,
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
                    paymentTransaction.UserInfo = userInfo;
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
                paymentTransaction.UserInfo = userInfo;
                paymentTransaction.Result = result;
                _paymentTransactions.Add(paymentTransaction);
                result.Message += SaveTransactions();
            }
            return result;
        }
        public ResultObj CreateUserSubscription(Stripe.Checkout.Session session)
        {
            var result = new ResultObj();
            var userInfo = new UserInfo();
            var paymentTransaction = new PaymentTransaction()
            {
                Id = _paymentTransactions.Max(m => m.Id) + 1,
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
                    paymentTransaction.UserInfo = userInfo;
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
                paymentTransaction.UserInfo = userInfo;
                paymentTransaction.Result = result;
                _paymentTransactions.Add(paymentTransaction);
                result.Message += SaveTransactions();
            }
            return result;
        }
        public ResultObj WakeUp()
        {
            var result = new ResultObj();
            result.Message = " Service : WakeUp ";
            try
            {
                PublishRepo.PaymentReady(_logger, _rabbitRepo, true);
                result.Message += " Published paymentServiceReady event ";
                result.Success = true;
                _logger.Info(result.Message);
            }
            catch (Exception e)
            {
                result.Message += " Failed to publish paymentServiceReady event . Error was : " + e.ToString();
                result.Success = false;
                _logger.Error(result.Message);
            }
            return result;
        }
    }
}