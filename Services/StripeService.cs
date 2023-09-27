using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.Factory;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Utils.Helpers;
using Microsoft.Extensions.Options;
using MetroLog;
using Stripe;
using Stripe.Checkout;

namespace NetworkMonitor.Payment.Services
{
    public interface IStripeService
    {
        Dictionary<string, string> SessionList { get; set; }
        Task<ResultObj> WakeUp();
        Task<ResultObj> PaymentCheck();
        Task<ResultObj> PaymentComplete(PaymentTransaction paymentTransaction);
        Task<ResultObj> PingInfosComplete(PaymentTransaction paymentTransaction);
        Task<ResultObj> RegisterUser(RegisteredUser RegisteredUser);
        Task<ResultObj> UpdateUserSubscription(Subscription session);
        Task<ResultObj> CreateUserSubscription(Stripe.Checkout.Session session);
        Task Init();
        ConcurrentBag<RegisteredUser> RegisteredUsers { get; }
    }
    public class StripeService : IStripeService
    {
        CancellationToken _token;
        private Dictionary<string, string> _sessionList = new Dictionary<string, string>();
        private ConcurrentBag<PaymentTransaction> _paymentTransactions = new ConcurrentBag<PaymentTransaction>();
        private List<IRabbitRepo> _rabbitRepos = new List<IRabbitRepo>();
        private ILogger _logger;
        private IFileRepo _fileRepo;
        private INetLoggerFactory _loggerFactory;
        private ISystemParamsHelper _systemParamsHelper;

        private ConcurrentBag<RegisteredUser> _registeredUsers = new ConcurrentBag<RegisteredUser>();
        public Dictionary<string, string> SessionList { get => _sessionList; set => _sessionList = value; }
        public ConcurrentBag<RegisteredUser> RegisteredUsers { get => _registeredUsers; }
        public readonly IOptions<PaymentOptions> options;
        public StripeService(INetLoggerFactory loggerFactory,ISystemParamsHelper systemParamsHelper, IOptions<PaymentOptions> options, CancellationTokenSource cancellationTokenSource, IFileRepo fileRepo)
        {
            _loggerFactory=loggerFactory;
            _systemParamsHelper=systemParamsHelper;
            _token = cancellationTokenSource.Token;
            _fileRepo = fileRepo;
            this.options = options;
            _logger = loggerFactory.GetLogger("StripeService");
        }
        public async Task Init()
        {

            var result = new ResultObj();
            result.Message = " Init Stripe Service : ";
            try
            {
                _token.Register(() => this.Shutdown());
                _fileRepo.CheckFileExists("PaymentTransactions", _logger);
                _fileRepo.CheckFileExists("RegisteredUsers", _logger);
                _paymentTransactions = await _fileRepo.GetStateJsonAsync<ConcurrentBag<PaymentTransaction>>("PaymentTransactions");
                _registeredUsers = await _fileRepo.GetStateJsonAsync<ConcurrentBag<RegisteredUser>>("RegisteredUsers");


                if (_paymentTransactions != null)
                {
                    result.Message += " Loaded " + _paymentTransactions.Count + " PaymentTranctions from State. ";
                }
                
                if (_registeredUsers != null)
                {
                    result.Message += " Loaded " + _registeredUsers.Count + " RegisteredUsers from State. ";
                }
                
                result.Success = true;
            }
            catch (Exception e)
            {
              
                result.Success = false;
                result.Message += " Error Loading State . Error was : " + e.ToString() + " . ";
            }
            finally{
                  if (_paymentTransactions == null)
                {
                    _paymentTransactions = new ConcurrentBag<PaymentTransaction>();
                    result.Message += " Failed to load PaymentTransactions from State. Setting new ConcurrentBag<PaymentTransaction>() ";
                }
                if (_registeredUsers == null)
                {
                    _registeredUsers = new ConcurrentBag<RegisteredUser>();
                    result.Message += " Failed to load  RegisteredUsers from State. Setting new ConcurrentBag<RegisteredUser>() ";
                }
            }
            try
            {
                this.options.Value.SystemUrls.ForEach(f =>
                {
                    ISystemParamsHelper paymentParamsHelper=new PaymentParamsHelper(f);
                    _logger.Info(" : StripeService : Init : Adding IRabbitRepo for : " + f.ExternalUrl + " : ");
                    _rabbitRepos.Add(new RabbitRepo(_loggerFactory, paymentParamsHelper));
                });
            }
            catch (Exception e)
            {
                result.Message += " Could not setup RabbitListner. Error was : " + e.ToString() + " . ";
                result.Success = false;
            }
            try
            {
                var updateProductObj=new UpdateProductObj(){
                    Products=this.options.Value.StripeProducts,
                    PaymentServerUrl=_systemParamsHelper.GetSystemParams().ThisSystemUrl.ExternalUrl
                };
                await PublishRepo.UpdateProductsAsync(_logger, _rabbitRepos, updateProductObj);
            }
            catch (Exception e)
            {
                result.Message += " Could Publish product list to Monitor Services. Error was : " + e.ToString() + " . ";
                result.Success = false;
            }
            if (_paymentTransactions == null)
            {
                _paymentTransactions = new ConcurrentBag<PaymentTransaction>();
            }
            /*if (_paymentTransactions.Count == 0)
            {
                _paymentTransactions.Add(new PaymentTransaction());
            }*/
            result.Message += " Finished StripeService Init ";
            result.Success = result.Success && true;
            if (result.Success)
                _logger.Info(result.Message);
            else _logger.Fatal(result.Message);

        }
        public void Shutdown()
        {
            _logger.Warn(" : SHUTDOWN started :");
            var result = SaveTransactions().Result;
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
        // A method that takes the paramter registerUser and adds it to the list of registerd users. Checing if it already exists first use UserId and CustomerId to match.
        public async Task<ResultObj> RegisterUser(RegisteredUser registeredUser)
        {
            var result = new ResultObj();
            result.Message = " SERVICE : Register User : ";
            if (_registeredUsers.Where(w => w.UserId == registeredUser.UserId || w.CustomerId == registeredUser.CustomerId).Count() == 0)
            {
                _registeredUsers.Add(registeredUser);
                result.Message += " Added User : " + registeredUser.UserId + " : " + registeredUser.CustomerId + " : " + registeredUser.ExternalUrl + " : ";
            }
            else
            {
                // Update the existing user.
                var user = _registeredUsers.Where(w => w.UserId == registeredUser.UserId || w.CustomerId == registeredUser.CustomerId).FirstOrDefault();
                user.CustomerId = registeredUser.CustomerId;
                user.UserId = registeredUser.UserId;
                user.ExternalUrl = registeredUser.ExternalUrl;
                user.UserEmail = registeredUser.UserEmail;
            }
            await SaveRegisteredUsers(result);
            return result;
        }
        // A Method to return the ExternalUrl from RegisteredUser list using the UserId or CustomerId.
        public string GetExternalUrl(string userId, string customerId)
        {
            var result = "";
            if (_registeredUsers.Where(w => w.UserId == userId || w.CustomerId == customerId).Count() > 0)
            {
                result = _registeredUsers.Where(w => w.UserId == userId || w.CustomerId == customerId).FirstOrDefault().ExternalUrl;
            }
            return result;
        }
        public async Task<ResultObj> PaymentCheck()
        {
            var result = new ResultObj();
            try
            {
                // get a list of PaymentTransactions that are not complete and order by IsUpdate then EventDate.
                _paymentTransactions.Where(w => w.IsComplete == false).OrderBy(o => o.IsUpdate).ThenBy(o => o.EventDate).ToList().ForEach(async p =>
                {
                    // Using p.IsUpdate to determine if this is an update or a new subscription. Publish to RabbitMQ.
                    if (p.IsUpdate)
                    {
                        await PublishRepo.UpdateUserSubscriptionAsync(_logger, _rabbitRepos, p);
                    }
                    else
                    {
                        await PublishRepo.CreateUserSubscriptionAsync(_logger, _rabbitRepos, p);
                    }
                    result.Message += (" Retry " + p.RetryCount + " of Payment Transaction  for Customer " + p.UserInfo.CustomerId + " : " + (p.IsUpdate ? "Updated" : "Created") + " : " + p.UserInfo.UserID + " : " + p.Id + " : " + p.EventDate + " . ");
                    p.RetryCount++;
                    if (p.RetryCount > 5)
                    {
                        //TODO notify user of failure.
                        _logger.Error(" Payment Transaction Failed for Customer " + p.UserInfo.CustomerId + " : " + (p.IsUpdate ? "Updated" : "Created") + " : " + p.UserInfo.UserID + " : " + p.Id + " : " + p.EventDate + " . ");
                    }
                    await SaveTransactions();
                    Task.Delay(500).Wait();
                });
                await PublishRepo.PaymentReadyAsync(_logger, _rabbitRepos, true);
                result.Message += " Payment Transaction Queue Checked ";
                result.Success = true;
                //_logger.Info(result.Message);
            }
            catch (Exception e)
            {
                result.Message = " Failed to check Payment Transaction Queue . Error was : " + e.ToString();
                result.Success = false;
                _logger.Error(result.Message);
            }
            return result;
        }
        private async Task<ResultObj> SaveRegisteredUsers(ResultObj result)
        {
            try
            {
                await _fileRepo.SaveStateJsonAsync<ConcurrentBag<RegisteredUser>>("RegisteredUsers", _registeredUsers);
                result.Message += " Save RegisteredUsers Completed ";
                result.Success = true;
            }
            catch (Exception e)
            {
                result.Message += " Failed to Save RegisteredUsers . Error was : " + e.ToString();
                result.Success = false;
                _logger.Error(result.Message);
            }
            return result;
        }
        private async Task<ResultObj> SaveTransactions()
        {
            var result = new ResultObj();
            try
            {
                await _fileRepo.SaveStateJsonAsync<ConcurrentBag<PaymentTransaction>>("PaymentTransactions", _paymentTransactions);
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
        public async Task<ResultObj> PaymentComplete(PaymentTransaction paymentTransaction)
        {
            var result = new ResultObj();
            try
            {
                var updatePaymentTransaction = _paymentTransactions.Where(w => w.Id == paymentTransaction.Id).FirstOrDefault();
                if (updatePaymentTransaction != null)
                {
                    // If transaction is alreay complete just log the result.
                    if (updatePaymentTransaction.IsComplete)
                    {
                        result.Message = " !!Already completed.";
                        result.Success = true;
                        return result;
                    }
                    updatePaymentTransaction.Result = paymentTransaction.Result;
                    if (paymentTransaction.IsComplete)
                    {
                        updatePaymentTransaction.IsComplete = true;
                        updatePaymentTransaction.CompletedDate = DateTime.Now;
                        // log the payment transaction to result.Message. Showing Created or Updatee, UserInfo, ID and the EventDate.
                        result.Message += " Payment Complete => Payment Transaction for Customer " + paymentTransaction.UserInfo.CustomerId + " : " + (paymentTransaction.IsUpdate ? "Updated" : "Created") + " : " + paymentTransaction.UserInfo.UserID + " : " + paymentTransaction.Id + " : " + paymentTransaction.EventDate;
                        result.Success = true;
                    }
                    else
                    {
                        var resultService = paymentTransaction.Result;
                        if (resultService == null)
                        {
                            result.Message += " Monitor Serivce Result for Transaction with ID " + paymentTransaction.Id + " Fail Error was : " + " Result is null ";
                            result.Success = false;
                        }
                        else
                        {
                            string? message = paymentTransaction.Result.Message;
                            result.Message += " Monitor Serivce Result for Transaction with ID " + paymentTransaction.Id + " Fail Error was : " + message;
                            if (resultService.Data != null)
                            {
                                result.Message += " :: " + resultService.Data.ToString();
                            }
                            result.Success = false;
                        }
                    }
                    await SaveTransactions();
                }
                else
                {
                    result.Message = " Failed to Find PaymentTransaction with Id " + paymentTransaction.Id;
                    result.Success = false;
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

           public async Task<ResultObj> PingInfosComplete(PaymentTransaction paymentTransaction)
        {
            var result = new ResultObj();
            try
            {
                var updatePaymentTransaction = _paymentTransactions.Where(w => w.Id == paymentTransaction.Id).FirstOrDefault();
                if (updatePaymentTransaction != null)
                {
                    // If transaction is alreay complete just log the result.
                    if (updatePaymentTransaction.PingInfosComplete)
                    {
                        result.Message = " !! PingInfos Already completed.";
                        result.Success = true;
                        return result;
                    }
                    updatePaymentTransaction.Result = paymentTransaction.Result;
                    if (paymentTransaction.PingInfosComplete)
                    {
                        updatePaymentTransaction.PingInfosComplete = true;
                        result.Message += " PingInfos Complete => Payment Transaction for Customer " + paymentTransaction.UserInfo.CustomerId + " : " + (paymentTransaction.IsUpdate ? "Updated" : "Created") + " : " + paymentTransaction.UserInfo.UserID + " : " + paymentTransaction.Id + " : " + paymentTransaction.EventDate;
                        result.Success = true;
                    }
                    else
                    {
                        var resultService = paymentTransaction.Result;
                        if (resultService == null)
                        {
                            result.Message += " PingInfo Serivce Result for Transaction with ID " + paymentTransaction.Id + " Fail Error was : " + " Result is null ";
                            result.Success = false;
                        }
                        else
                        {
                            string? message = paymentTransaction.Result.Message;
                            result.Message += " PingInfo Serivce Result for Transaction with ID " + paymentTransaction.Id + " Fail Error was : " + message;
                            if (resultService.Data != null)
                            {
                                result.Message += " :: " + resultService.Data.ToString();
                            }
                            result.Success = false;
                        }
                    }
                    await SaveTransactions();
                }
                else
                {
                    result.Message = " Failed to Find PaymentTransaction with Id " + paymentTransaction.Id;
                    result.Success = false;
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
     
        public async Task<ResultObj> UpdateUserSubscription(Subscription session)
        {
            var result = new ResultObj();
            var userInfo = new UserInfo();
            int id = 0;
            if (_paymentTransactions.Count > 0)
            {
                id = _paymentTransactions.Max(m => m.Id);
            }
            var paymentTransaction = new PaymentTransaction()
            {
                Id = id + 1,
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
                    ProductObj paymentObj = this.options.Value.StripeProducts.Where(w => w.PriceId == item.Price.Id).FirstOrDefault();
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
                string externalUrl = GetExternalUrl("", userInfo.CustomerId);
                paymentTransaction.ExternalUrl = externalUrl;
                if (userInfo.CustomerId != null)
                {
                    paymentTransaction.UserInfo = userInfo;
                    await PublishRepo.UpdateUserSubscriptionAsync(_logger, _rabbitRepos, paymentTransaction);
                    result.Message += " Success : Published event UpdateUserSubscription ";
                     await PublishRepo.UpdateUserPingInfosAsync(_logger, _rabbitRepos, paymentTransaction);
                    result.Message += " Success : Published event UpdateUserPingInfos ";
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
                result.Message += await SaveTransactions();
            }
            return result;
        }
        public async Task<ResultObj> CreateUserSubscription(Stripe.Checkout.Session session)
        {
            var result = new ResultObj();
            var userInfo = new UserInfo();
            int id = 0;
            if (_paymentTransactions.Count > 0)
            {
                id = _paymentTransactions.Max(m => m.Id);
            }
            var paymentTransaction = new PaymentTransaction()
            {
                Id = id + 1,
                EventDate = DateTime.UtcNow,
                UserInfo = userInfo,
                IsUpdate = false,
                IsComplete = false,
                Result = result,
            };
            result.Message = "SERVICE : CreateUserSubscription : ";
            try
            {
                if (_sessionList.ContainsKey(session.Id))
                {
                    // If the session.Id exists in the dictionary, retrieve its associated value.
                    userInfo.UserID = _sessionList[session.Id];
                }
                else
                {
                    string newUserID = session.ClientReferenceId;
                    _sessionList[session.Id] = newUserID;
                    userInfo.UserID = newUserID;
                }
                userInfo.CustomerId = session.CustomerId;
                userInfo.Email = session.CustomerEmail;
                string externalUrl = GetExternalUrl(userInfo.UserID, userInfo.CustomerId);
                var RegisteredUser = new RegisteredUser()
                {
                    CustomerId = userInfo.CustomerId,
                    UserId = userInfo.UserID,
                    UserEmail = userInfo.Email,
                    ExternalUrl = externalUrl
                };
                RegisterUser(RegisteredUser);
                paymentTransaction.ExternalUrl = externalUrl;
                if (userInfo.UserID != null)
                {
                    paymentTransaction.UserInfo = userInfo;
                    await PublishRepo.CreateUserSubscriptionAsync(_logger, _rabbitRepos, paymentTransaction);
                    result.Message += "Success : Published event CreateUserSubscription";
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
        public async Task<ResultObj> WakeUp()
        {
            var result = new ResultObj();
            result.Message = " Service : WakeUp ";
            try
            {
                await PublishRepo.PaymentReadyAsync(_logger, _rabbitRepos, true);
                result.Message += " Received Wakeup so Published paymentServiceReady event ";
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