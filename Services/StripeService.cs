using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.Factory;
using NetworkMonitor.Utils.Helpers;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Utils.Helpers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;
using Org.BouncyCastle.Crypto.Digests;

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
        Task<ResultObj> UpdateCustomerID(RegisteredUser registeredUser);
        Task<ResultObj> DeleteCustomerID(RegisteredUser registeredUser, string eventId);
        Task<TResultObj<string>> ProcessPaymentLink(string email, string paymentLinkId, string eventId);
        Task<TResultObj<string>> UpdateUserCustomerId(string customerId, string eventId, bool blankCustomerId = false);
        Task<TResultObj<string>> UpdateUserSubscription(string customerId, string eventId, string priceId, DateTime? cancelAt);
        Task<TResultObj<string>> DeleteUserSubscription(string customerId, string eventId);
        Task Init();
        ConcurrentBag<RegisteredUser> RegisteredUsers { get; }
    }
    public class StripeService : IStripeService
    {
        CancellationToken _token;
        private Dictionary<string, string> _sessionList = new Dictionary<string, string>();
        private List<PaymentTransaction> _paymentTransactions = new List<PaymentTransaction>();
        private List<IRabbitRepo> _rabbitRepos = new List<IRabbitRepo>();
        private List<IRabbitListener> _rabbitListeners = new List<IRabbitListener>();
        private ILogger _logger;
        private IFileRepo _fileRepo;
        private ILoggerFactory _loggerFactory;
        private ISystemParamsHelper _systemParamsHelper;

        private ConcurrentBag<RegisteredUser> _registeredUsers = new ConcurrentBag<RegisteredUser>();
        public Dictionary<string, string> SessionList { get => _sessionList; set => _sessionList = value; }
        public ConcurrentBag<RegisteredUser> RegisteredUsers { get => _registeredUsers; }
        public readonly IOptions<PaymentOptions> options;
        public StripeService(ILogger<StripeService> logger, ILoggerFactory loggerFactory, ISystemParamsHelper systemParamsHelper, IOptions<PaymentOptions> options, CancellationTokenSource cancellationTokenSource, IFileRepo fileRepo)
        {
            _systemParamsHelper = systemParamsHelper;
            _token = cancellationTokenSource.Token;
            _fileRepo = fileRepo;
            this.options = options;
            _logger = logger;
            _loggerFactory = loggerFactory;
        }
        public async Task Init()
        {

            var result = new ResultObj();
            result.Message = " STRIPESERVICE : Init : ";
            try
            {
                _token.Register(() => this.Shutdown());
                _fileRepo.CheckFileExistsWithCreateObject<List<PaymentTransaction>>("PaymentTransactions", new List<PaymentTransaction>(), _logger);
                _fileRepo.CheckFileExistsWithCreateObject<List<RegisteredUser>>("RegisteredUsers", new List<RegisteredUser>(), _logger);

                var paymentTransactionsList = await _fileRepo.GetStateJsonAsync<List<PaymentTransaction>>("PaymentTransactions");
                if (paymentTransactionsList == null)
                {
                    _logger.LogWarning(" Warning : PaymentTransactions data is null. ");
                    _paymentTransactions = new List<PaymentTransaction>();
                }
                else
                {
                    _paymentTransactions = new List<PaymentTransaction>(paymentTransactionsList);
                }

                var registeredUsersList = await _fileRepo.GetStateJsonAsync<List<RegisteredUser>>("RegisteredUsers");
                if (registeredUsersList == null)
                {
                    _logger.LogWarning(" Warning : RegisteredUsers data is null. ");
                    _registeredUsers = new ConcurrentBag<RegisteredUser>();
                }
                else
                {
                    _registeredUsers = new ConcurrentBag<RegisteredUser>(registeredUsersList);
                }

                if (_paymentTransactions != null)
                {
                    result.Message += " Success: Loaded " + _paymentTransactions.Count + " PaymentTranctions from State. ";
                }

                if (_registeredUsers != null)
                {
                    result.Message += " Success :Loaded " + _registeredUsers.Count + " RegisteredUsers from State. ";
                }

                result.Success = true;
            }
            catch (Exception e)
            {

                result.Success = false;
                result.Message += " Error : Loading State . Error was : " + e.Message + " . ";
            }
            finally
            {
                if (_paymentTransactions == null)
                {
                    _paymentTransactions = new List<PaymentTransaction>();
                    result.Message += " Error : Failed to load PaymentTransactions from State. Setting new List<PaymentTransaction>() ";
                }
                if (_registeredUsers == null)
                {
                    _registeredUsers = new ConcurrentBag<RegisteredUser>();
                    result.Message += " Error : Failed to load  RegisteredUsers from State. Setting new ConcurrentBag<RegisteredUser>() ";
                }
            }
            try
            {
                foreach (var f in this.options.Value.SystemUrls)        
                {
                    ISystemParamsHelper localSystemParamsHelper = new LocalSystemParamsHelper(f);
                    _logger.LogInformation(" Adding RabbitRepo for : " + f.ExternalUrl + " . ");
                    var rabbitRepo=new RabbitRepo(_loggerFactory.CreateLogger<RabbitRepo>(), localSystemParamsHelper);
                    await rabbitRepo.ConnectAndSetUp();
                    _rabbitRepos.Add(rabbitRepo);
                    _rabbitListeners.Add(new RabbitListener(this, _loggerFactory.CreateLogger<RabbitListener>(), localSystemParamsHelper));
                }
            }
            catch (Exception e)
            {
                result.Message += " Error : Could not setup RabbitRepos. Error was : " + e.ToString() + " . ";
                result.Success = false;
            }
            try
            {
                var updateProductObj = new UpdateProductObj()
                {
                    Products = this.options.Value.StripeProducts,
                    PaymentServerUrl = this.options.Value.LocalSystemUrl.ExternalUrl
                };
                await PublishRepo.UpdateProductsAsync(_logger, _rabbitRepos, updateProductObj);
            }
            catch (Exception e)
            {
                result.Message += " Error : Could not Publish product list to Monitor Services. Error was : " + e.ToString() + " . ";
                result.Success = false;
            }
            if (_paymentTransactions == null)
            {
                _paymentTransactions = new List<PaymentTransaction>();
            }
            /*if (_paymentTransactions.Count == 0)
            {
                _paymentTransactions.Add(new PaymentTransaction());
            }*/
            result.Message += " Finished StripeService Init . ";
            result.Success = result.Success && true;
            if (result.Success)
                _logger.LogInformation(result.Message);
            else _logger.LogCritical(result.Message);

        }
        public void Shutdown()
        {
            _logger.LogWarning(" : SHUTDOWN started :");
            var result = SaveTransactions().Result;
            if (result.Success)
            {
                _logger.LogInformation(result.Message);
            }
            else
            {
                _logger.LogError(result.Message);
            }
            _logger.LogWarning(" : SHUTDOWN completed :");
        }
        // A method that takes the paramter registerUser and adds it to the list of registerd users. Checing if it already exists first use UserId and CustomerId to match.
        public async Task<ResultObj> RegisterUser(RegisteredUser registeredUser)
        {
            var result = new ResultObj();
            result.Message = " STRIPESERVICE : UpdateRegisteredUser : ";
            var user = _registeredUsers.Where(w => w.UserEmail == registeredUser.UserEmail || w.UserId == registeredUser.UserId).FirstOrDefault();

            if (user == null)
            {
                _registeredUsers.Add(registeredUser);
                result.Message += " Success : Added User : " + registeredUser.UserId + " : " + registeredUser.UserEmail + " : " + registeredUser.ExternalUrl + " . ";
            }
            else
            {
                // Update the existing user.
                //user.CustomerId = registeredUser.CustomerId;
                user.UserId = registeredUser.UserId;
                user.ExternalUrl = registeredUser.ExternalUrl;
                user.UserEmail = registeredUser.UserEmail;
            }
            await SaveRegisteredUsers(result);
            return result;
        }

        // A method that takes the paramter registerUser and adds it to the list of registerd users. Checing if it already exists first use UserId and CustomerId to match.
        public async Task<ResultObj> UpdateCustomerID(RegisteredUser registeredUser)
        {
            var result = new ResultObj();
            result.Message = " STRIPESERVICE : UpdateCustomerID : ";
            result.Success = false;
            var user = _registeredUsers.Where(w => w.UserId == registeredUser.UserId || w.UserEmail == registeredUser.UserEmail).FirstOrDefault();

            if (user == null)
            {
                result.Message += " Error : Could not find user to update CustomerId : available user data was : " + registeredUser.UserId + " : " + registeredUser.UserEmail + " . ";

            }
            else
            {

                user.CustomerId = registeredUser.CustomerId;
                result.Message += " Updating Customer Id for User : " + registeredUser.UserId + " : " + registeredUser.UserEmail + " : " + registeredUser.ExternalUrl + " : " + registeredUser.CustomerId;
                var saveResult = await SaveRegisteredUsers(result);
                result.Success = saveResult.Success;
                result.Message += saveResult.Message;
            }

            return result;
        }

        public async Task<ResultObj> DeleteCustomerID(RegisteredUser registeredUser, string eventId)
        {
            var result = new ResultObj();
            result.Message = " STRIPESERVICE : DeleteCustomerID : ";
            result.Success = false;
            var user = _registeredUsers.Where(w => w.UserId == registeredUser.UserId || w.UserEmail == registeredUser.UserEmail).FirstOrDefault();

            if (user == null)
            {
                result.Message += " Error : Could not find user to delete CustomerId : available user data was : " + registeredUser.UserId + " : " + registeredUser.UserEmail + " . ";

            }
            else
            {

                //user.CustomerId = "";
                result.Message += " Deleting Customer Id for User : " + registeredUser.UserId + " : " + registeredUser.UserEmail + " : " + registeredUser.ExternalUrl + " : " + registeredUser.CustomerId + " . ";
                //var saveResult = await SaveRegisteredUsers(result);

                //result.Message += saveResult.Message;
                // Send Blank customerId to Monitor Service. 
                var updateResult = await UpdateUserCustomerId(registeredUser.CustomerId, eventId, true);
                result.Message += updateResult.Message;
                result.Success = updateResult.Success;

            }
            //result.Success=true;
            return result;
        }

        public async Task<ResultObj> PaymentCheck()
        {
            var result = new ResultObj();
            result.Message = " STRIPESERVICE : PaymentCheck : ";
            try
            {
                // get a list of PaymentTransactions that are not complete and order by IsUpdate then EventDate.
                foreach (var p in _paymentTransactions.Where(w => w.IsComplete == false).OrderBy(o => o.IsUpdate).ThenBy(o => o.EventDate).ToList())
                {
                    result.Message += " Warning : Retry " + p.RetryCount + " of Payment Transaction  for Customer " + p.UserInfo.CustomerId + " : " + (p.IsUpdate ? "Updated" : "Created") + " : " + p.UserInfo.UserID + " : " + p.Id + " : " + p.EventDate + " . ";

                    // Using p.IsUpdate to determine if this is an update or a new subscription. Publish to RabbitMQ.
                    if (p.UserInfo.CustomerId != null)
                    {
                        if (p.IsUpdate)
                        {
                            await UpdateUserSubscription(p.UserInfo.CustomerId, p.EventId, p.PriceId, p.UserInfo.CancelAt);
                        }
                        if (p.IsCreate)
                        {
                            await UpdateUserCustomerId(p.UserInfo.CustomerId, p.EventId);
                        }
                        if (p.IsDelete)
                        {
                            await DeleteUserSubscription(p.UserInfo.CustomerId, p.EventId);
                        }
                        
                    }
                    else
                    {
                        if (p.UserInfo.UserID != null) result.Message += " Warning : Retry with CustomerID=null" + p.UserInfo.UserID;
                        else result.Message += " Warning : Retry with CustomerID=null and UserID=null .";
                    }
                     if (!string.IsNullOrEmpty(p.UserInfo.Email) && p.IsPayment)
                    {
                            var email=p.UserInfo.Email;
                            await ProcessPaymentLink(email!, p.PriceId,p.EventId);
                        
                    }
                    else if (p.IsPayment){
                        result.Message += " Warning : Retrying payment with no email" + p.UserInfo.UserID;
                       
                    }
                    

                    p.RetryCount++;
                    if (p.RetryCount > 5)
                    {
                        //TODO notify user of failure.
                        _logger.LogError(" Error : Payment Transaction Failed for Customer " + p.UserInfo.CustomerId + " : " + (p.IsUpdate ? "Updated" : "Created") + " : " + p.UserInfo.UserID + " : " + p.Id + " : " + p.EventDate + " . ");
                    }
                    await SaveTransactions();
                    await Task.Delay(500);
                }
                await PublishRepo.PaymentReadyAsync(_logger, _rabbitRepos, true);
                result.Message += " Success : Payment Transaction Queue Checked . ";
                result.Success = true;
                //_logger.LogInformation(result.Message);
            }
            catch (Exception e)
            {
                result.Message += " Error : Failed to check Payment Transaction Queue . Error was : " + e.Message + " . ";
                result.Success = false;
                _logger.LogError(result.Message);
            }
            return result;
        }
        private async Task<ResultObj> SaveRegisteredUsers(ResultObj result)
        {
            try
            {
                await _fileRepo.SaveStateJsonAsync<List<RegisteredUser>>("RegisteredUsers", _registeredUsers.ToList());
                result.Message += " Success : Save RegisteredUsers Completed . ";
                result.Success = true;
            }
            catch (Exception e)
            {
                result.Message += " Error : Failed to Save RegisteredUsers . Error was : " + e.Message + " . ";
                result.Success = false;
                _logger.LogError(result.Message);
            }
            return result;
        }
        private async Task<ResultObj> SaveTransactions()
        {
            var result = new ResultObj();
            try
            {
                await _fileRepo.SaveStateJsonAsync<List<PaymentTransaction>>("PaymentTransactions", _paymentTransactions);
                result.Message = " Success : Save Transactions Completed . ";
                result.Success = true;
            }
            catch (Exception e)
            {
                result.Message = " Error : Failed to Save Transactions. Error was : " + e.Message + " . ";
                result.Success = false;
                _logger.LogError(result.Message);
            }
            return result;
        }
        public async Task<ResultObj> PaymentComplete(PaymentTransaction paymentTransaction)
        {
            var result = new ResultObj();
            result.Message = " STRIPESERVICE : PaymentComplete : ";
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
                    updatePaymentTransaction.IsUpdate = paymentTransaction.IsUpdate;
                    if (paymentTransaction.Result.Success)
                    {
                        updatePaymentTransaction.IsComplete = true;
                        updatePaymentTransaction.CompletedDate = DateTime.Now;
                        // log the payment transaction to result.Message. Showing Created or Updatee, UserInfo, ID and the EventDate.
                        result.Message += " Transaction  complete for Customer " + paymentTransaction.UserInfo.CustomerId + " : " + (paymentTransaction.IsUpdate ? "Updated" : "Created") + " : " + paymentTransaction.UserInfo.UserID + " : " + paymentTransaction.Id + " : " + paymentTransaction.EventDate + " : Result : " + paymentTransaction.Result.Message;
                        try
                        {
                            if (updatePaymentTransaction.IsUpdate)
                            {
                                await PublishRepo.UpdateUserPingInfosAsync(_logger, _rabbitRepos, updatePaymentTransaction);
                                result.Message += " Success : Published event UpdateUserPingInfos ";
                            }
                            result.Success = true;

                        }
                        catch (Exception e)
                        {
                            result.Message += "Error : failed Publish UpdateUserPingInfos . Error was : " + e.Message;
                            _logger.LogError("Error : failed Publish UpdateUserPingInfos . Error was : " + e.ToString());
                            result.Success = false;
                        }
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
                _logger.LogError(result.Message);
            }
            return result;
        }

        public async Task<ResultObj> PingInfosComplete(PaymentTransaction paymentTransaction)
        {
            var result = new ResultObj();
            result.Message = " STRIPESERVICE : PingInfosComplete : ";
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
                    if (paymentTransaction.Result.Success)
                    {
                        updatePaymentTransaction.PingInfosComplete = true;
                        result.Message += " PingInfos Complete => Payment Transaction for Customer " + paymentTransaction.UserInfo.CustomerId + " : " + (paymentTransaction.IsUpdate ? "Updated" : "Created") + " : " + paymentTransaction.UserInfo.UserID + " : " + paymentTransaction.Id + " : " + paymentTransaction.EventDate + " : Result : " + paymentTransaction.Result.Message;
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
                _logger.LogError(result.Message);
            }
            return result;
        }

        private (UserInfo, RegisteredUser) GetUserFromCustomerId(string customerId)
        {
            var registeredUser = _registeredUsers.Where(w => w.CustomerId == customerId).FirstOrDefault();
            if (registeredUser != null)
            {
                return new(new UserInfo()
                {
                    UserID = registeredUser.UserId,
                    CustomerId = registeredUser.CustomerId,
                    Email = registeredUser.UserEmail
                }, registeredUser);
            }
            return (new UserInfo() { CustomerId = customerId }, new RegisteredUser());
        }
         private (UserInfo, RegisteredUser) GetUserFromEmail(string email)
        {
            var registeredUser = _registeredUsers.Where(w => w.UserEmail == email).FirstOrDefault();
            if (registeredUser != null)
            {
                return new(new UserInfo()
                {
                    UserID = registeredUser.UserId,
                    CustomerId = registeredUser.CustomerId,
                    Email = registeredUser.UserEmail
                }, registeredUser);
            }
            return (new UserInfo() { Email=email }, new RegisteredUser());
        }
       
        public async Task<TResultObj<string>> UpdateUserSubscription(string customerId, string eventId, string priceId, DateTime? cancelAt)
        {
            var result = new TResultObj<string>();
            result.Success = false;
            result.Message = " STRIPESERVICE : UpdateUserSubscription : ";
            result.Message += " CustomerId = " + customerId + " . ";

            var userObj = GetUserFromCustomerId(customerId);
            var userInfo = userObj.Item1;
            var registeredUser = userObj.Item2;

            userInfo.CancelAt = cancelAt;
            bool foundProduct = false;



            var paymentObj = this.options.Value.StripeProducts.Where(w => w.PriceId == priceId).FirstOrDefault();
            if (paymentObj != null)
            {
                userInfo.AccountType = paymentObj.ProductName;
                userInfo.HostLimit = paymentObj.HostLimit;
                userInfo.CancelAt = cancelAt;
                result.Message += " Success : Changed CustomerID " + customerId + " Subsciption Product to " + paymentObj.ProductName;
                foundProduct = true;
            }
            else
            {
                result.Message += " Error : Failed to find Product with PriceID " + priceId;
                _logger.LogError(" Failed to find Product with PriceID " + priceId);
            }



            int id = 0;

            var paymentTransaction = _paymentTransactions.Where(w => w.EventId == eventId).FirstOrDefault();
            if (paymentTransaction == null)
            {
                if (_paymentTransactions.Count() > 0) id = _paymentTransactions.Max(m => m.Id);
                paymentTransaction = new PaymentTransaction()
                {
                    Id = id + 1,
                    EventDate = DateTime.UtcNow,
                    IsUpdate = true,
                    IsComplete = false,
                    Result = result,
                    PriceId = priceId,
                    EventId = eventId
                };
                _paymentTransactions.Add(paymentTransaction);
            }
            else
            {
                if (paymentTransaction.IsComplete)
                {
                    result.Success = true;
                    result.Message += " PaymentTransction already complete . Webhook was called again. ";
                    return result;
                }

            }
            paymentTransaction.UserInfo = userInfo;
            paymentTransaction.ExternalUrl = registeredUser.ExternalUrl;

            try
            {
                if (userInfo.UserID != null && foundProduct && registeredUser.ExternalUrl != "")
                {
                    result.Success = await PublishRepo.UpdateUserSubscriptionAsync(_logger, _rabbitRepos, paymentTransaction);
                    if (result.Success)
                    {
                        result.Message += "Success : Published event UpdateUserSubscription";
                        _logger.LogInformation(result.Message);
                    }
                    else
                    {
                        result.Message += "Error : Failed to Publish event UpdateUserSubscription";
                        _logger.LogError(result.Message);
                    }
                }
                else
                {
                    if (userInfo.UserID == null) result.Message += " Error : Did not send UpdateUserSubscriptoin userId is null. ";

                    if (!foundProduct) result.Message += " Error : Did not send UpdateUserSubscriptoin product not found. ";

                    if (registeredUser.ExternalUrl == "") result.Message += " Error : Did not send UpdateUserSubscription External Url is empty. ";

                    result.Success = false;
                    _logger.LogError(result.Message);
                }

            }
            catch (Exception e)
            {
                result.Message += "Error : failed publish UpdateUserSubscription . Error was : " + e.Message;
                _logger.LogError("Error : failed publish UpdateUserSubscription . Error was : " + e.ToString());
                result.Success = false;
            }
            finally
            {
                paymentTransaction.Result = result;
                //_paymentTransactions.Add(paymentTransaction);
                result.Message += SaveTransactions();
            }
            return result;
        }

        public async Task<TResultObj<string>> ProcessPaymentLink(string email, string paymentLinkId, string eventId)
        {
            var result = new TResultObj<string>();
            
                result = await BoostTokenForUser(email, eventId, paymentLinkId);
          
            return result;
        }

        private async Task<TResultObj<string>> BoostTokenForUser(string email, string eventId, string priceId)
        {
            var result = new TResultObj<string>();
            result.Success = false;
            result.Message = " STRIPESERVICE : BoostTokenForUserAsync : ";
            result.Message += " UserEmail = " + email + " . ";

            var userObj = GetUserFromEmail(email);
            var userInfo = userObj.Item1;
            var registeredUser = userObj.Item2;

            bool foundProduct = false;

            var paymentObj = this.options.Value.StripeProducts.Where(w => w.PriceId == priceId).FirstOrDefault();
            if (paymentObj != null)
            {
                userInfo.TokensUsed = paymentObj.Quantity;
                result.Message += " Success : Set User BoostTokens(TokensUsed) to" + paymentObj.Quantity;
                foundProduct = true;
            }
            else
            {
                result.Message += " Error : Failed to find Product with PriceID " + priceId;
                _logger.LogError(" Failed to find Product with PriceID " + priceId);
            }



            int id = 0;

            var paymentTransaction = _paymentTransactions.Where(w => w.EventId == eventId).FirstOrDefault();
            if (paymentTransaction == null)
            {
                if (_paymentTransactions.Count() > 0) id = _paymentTransactions.Max(m => m.Id);
                paymentTransaction = new PaymentTransaction()
                {
                    Id = id + 1,
                    EventDate = DateTime.UtcNow,
                    IsPayment = true,
                    IsComplete = false,
                    Result = result,
                    PriceId = priceId,
                    EventId = eventId,
                };
                _paymentTransactions.Add(paymentTransaction);
            }
            else
            {
                if (paymentTransaction.IsComplete)
                {
                    result.Success = true;
                    result.Message += " PaymentTransction already complete . Webhook was called again. ";
                    return result;
                }

            }
            paymentTransaction.UserInfo = userInfo;
            paymentTransaction.ExternalUrl = registeredUser.ExternalUrl;

            try
            {
                if (userInfo.UserID != null && foundProduct && registeredUser.ExternalUrl != "")
                {
                    result.Success = await PublishRepo.BoostTokenForUserAsync(_logger, _rabbitRepos, paymentTransaction);
                    if (result.Success)
                    {
                        result.Message += "Success : Published event BoostTokenForUser";
                        _logger.LogInformation(result.Message);
                    }
                    else
                    {
                        result.Message += "Error : Failed to Publish event BoostTokenForUser";
                        _logger.LogError(result.Message);
                    }
                }
                else
                {
                    if (userInfo.UserID == null) result.Message += " Error : Did not send BoostTokenForUser userId is null. ";

                    if (!foundProduct) result.Message += " Error : Did not send BoostTokenForUser product not found. ";

                    if (registeredUser.ExternalUrl == "") result.Message += " Error : Did not send BoostTokenForUser External Url is empty. ";

                    result.Success = false;
                    _logger.LogError(result.Message);
                }

            }
            catch (Exception e)
            {
                result.Message += "Error : failed publish BoostTokenForUser . Error was : " + e.Message;
                _logger.LogError("Error : failed publish BoostTokenForUser . Error was : " + e.ToString());
                result.Success = false;
            }
            finally
            {
                paymentTransaction.Result = result;
                //_paymentTransactions.Add(paymentTransaction);
                result.Message += SaveTransactions();
            }
            return result;
        }


        public async Task<TResultObj<string>> UpdateUserCustomerId(string customerId, string eventId, bool blankCustomerId = false)
        {
            var result = new TResultObj<string>();
            result.Success = false;
            result.Message = " STRIPESERVICE : UpdateUserCustomerId : ";
            result.Message += " CustomerId = " + customerId + " . ";

            var userObj = GetUserFromCustomerId(customerId);
            var userInfo = userObj.Item1;
            var registeredUser = userObj.Item2;
            if (blankCustomerId) userInfo.CustomerId = "";
            int id = 0;

            var paymentTransaction = _paymentTransactions.Where(w => w.EventId == eventId).FirstOrDefault();
            if (paymentTransaction == null)
            {
                if (_paymentTransactions.Count() > 0) id = _paymentTransactions.Max(m => m.Id);
                paymentTransaction = new PaymentTransaction()
                {
                    Id = id + 1,
                    EventDate = DateTime.UtcNow,
                    IsCreate = true,
                    IsComplete = false,
                    Result = result,
                    EventId = eventId
                };
                _paymentTransactions.Add(paymentTransaction);
            }
            else
            {
                if (paymentTransaction.IsComplete)
                {
                    result.Success = true;
                    result.Message += " PaymentTransction already complete . Webhook was called again. ";
                    return result;
                }

            }
            paymentTransaction.UserInfo = userInfo;
            paymentTransaction.ExternalUrl = registeredUser.ExternalUrl;

            try
            {

                if (userInfo.UserID != null && registeredUser.ExternalUrl != "")
                {
                    result.Success = await PublishRepo.UpdateUserCustomerIdAsync(_logger, _rabbitRepos, paymentTransaction);

                    if (result.Success)
                    {
                        result.Message += "Success : Published event UpdateUserCustomerId";
                        _logger.LogInformation(result.Message);
                    }
                    else
                    {
                        result.Message += "Error : Failed to Publish event UpdateUserCustomerId";
                        _logger.LogError(result.Message);
                    }
                }
                else
                {
                    if (userInfo.UserID == null) result.Message += " Error : Did not send UpdateUserCustomerId userId is null. ";
                    if (registeredUser.ExternalUrl == "") result.Message += " Error : Did not send UpdateUserCustomerId External Url is empty. ";
                    result.Success = false;
                    _logger.LogError(result.Message);
                }

            }
            catch (Exception e)
            {
                result.Message += "Error : failed publish UpdateUserCustomerId . Error was : " + e.Message;
                _logger.LogError("Error : failed publish UpdateUserCustomerId . Error was : " + e.ToString());
                result.Success = false;
            }
            finally
            {
                paymentTransaction.Result = result;
                //_paymentTransactions.Add(paymentTransaction);
                result.Message += SaveTransactions();
            }
            return result;
        }

        public async Task<TResultObj<string>> DeleteUserSubscription(string customerId, string eventId)
        {
            var result = new TResultObj<string>();
            result.Success = false;
            result.Message = " STRIPESERVICE : DeleteUserSubscription : ";
            result.Message += " CustomerId = " + customerId + " . ";

            var userObj = GetUserFromCustomerId(customerId);
            var userInfo = userObj.Item1;
            var registeredUser = userObj.Item2;

            var paymentObj = this.options.Value.StripeProducts.Where(w => w.PriceId == "price_free").FirstOrDefault();
            if (paymentObj != null)
            {
                userInfo.AccountType = paymentObj.ProductName;
                userInfo.HostLimit = paymentObj.HostLimit;
                userInfo.CancelAt = DateTime.UtcNow;
                userInfo.CustomerId = "";
                result.Message += " Success : Changed CustomerID " + customerId + " Subsciption Product to " + paymentObj.ProductName + " ";
            }
            else
            {
                result.Message += " Error : Failed to find Product with PriceID price_free";
                _logger.LogError(result.Message);
            }

            int id = 0;

            var paymentTransaction = _paymentTransactions.Where(w => w.EventId == eventId).FirstOrDefault();
            if (paymentTransaction == null)
            {
                if (_paymentTransactions.Count() > 0) id = _paymentTransactions.Max(m => m.Id);
                paymentTransaction = new PaymentTransaction()
                {
                    Id = id + 1,
                    EventDate = DateTime.UtcNow,
                    IsDelete = true,
                    IsComplete = false,
                    Result = result,
                    EventId = eventId
                };
                _paymentTransactions.Add(paymentTransaction);
            }
            else
            {
                if (paymentTransaction.IsComplete)
                {
                    result.Success = true;
                    result.Message += " PaymentTransction already complete . Webhook was called again. ";
                    return result;
                }

            }
            paymentTransaction.UserInfo = userInfo;
            paymentTransaction.ExternalUrl = registeredUser.ExternalUrl;

            try
            {
                if (userInfo.UserID != null && registeredUser.ExternalUrl != "")
                {
                    result.Success = await PublishRepo.UpdateUserSubscriptionAsync(_logger, _rabbitRepos, paymentTransaction);
                    if (result.Success)
                    {
                        result.Message += "Success : Published event DeleteUserSubscription";
                        _logger.LogInformation(result.Message);
                    }
                    else
                    {
                        result.Message += "Error : Failed to Publish event DeleteUserSubscription";
                        _logger.LogError(result.Message);
                    }
                }
                else
                {
                    if (userInfo.UserID == null) result.Message += " Error : Did not send DeleteUserSubscription userId is null. ";
                    if (registeredUser.ExternalUrl == "") result.Message += " Error : Did not send DeleteUserSubscription External Url is empty. ";
                    result.Success = false;
                    _logger.LogError(result.Message);
                }

            }
            catch (Exception e)
            {
                result.Message += "Error : failed DeleteUserSubscription . Error was : " + e.Message;
                _logger.LogError("Error : failed DeleteUserSubscription . Error was : " + e.ToString());
                result.Success = false;
            }
            finally
            {
                paymentTransaction.Result = result;
                //_paymentTransactions.Add(paymentTransaction);
                result.Message += SaveTransactions();
            }
            return result;
        }

        public async Task<ResultObj> WakeUp()
        {
            var result = new ResultObj();
            result.Message = " STRIPESERVICE : WakeUp : ";
            try
            {
                await PublishRepo.PaymentReadyAsync(_logger, _rabbitRepos, true);
                result.Message += " Received Wakeup so Published paymentServiceReady event ";
                result.Success = true;
                _logger.LogInformation(result.Message);
            }
            catch (Exception e)
            {
                result.Message += " Failed to publish paymentServiceReady event . Error was : " + e.ToString();
                result.Success = false;
                _logger.LogError(result.Message);
            }
            return result;
        }
    }
}