using System;
using System.Collections.Generic;
using System.Linq;
using NetworkMonitor.Objects.ServiceMessage;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
namespace NetworkMonitor.Objects.Repository
{
    public class PublishRepo
    {
        public static async Task UpdateProductsAsync(ILogger logger, List<IRabbitRepo> rabbitRepos, UpdateProductObj updateProductObj)
        {
            logger.LogInformation(" Publishing products : " + JsonConvert.SerializeObject(updateProductObj));
            // publish to all systems.
            foreach (IRabbitRepo rabbitRepo in rabbitRepos)
            {
                await rabbitRepo.PublishAsync<UpdateProductObj>("updateProducts", updateProductObj);
                logger.LogInformation(" Published event updateProducts to: " + rabbitRepo.SystemUrl);
            }
        }
        public static async Task PaymentReadyAsync(ILogger logger, List<IRabbitRepo> rabbitRepos, bool isReady)
        {
            var paymentInitObj = new PaymentServiceInitObj();
            paymentInitObj.IsPaymentServiceReady = isReady;
            // publish to all systems.
            foreach (IRabbitRepo rabbitRepo in rabbitRepos)
            {
                await rabbitRepo.PublishAsync<PaymentServiceInitObj>("paymentServiceReady", paymentInitObj);
                logger.LogInformation(" Published event PaymentServiceItitObj.IsPaymentServiceReady = " + isReady);
            }
        }
        public static async Task UpdateUserSubscriptionAsync(ILogger logger, List<IRabbitRepo> rabbitRepos, PaymentTransaction paymentTransaction)
        {
            try
            {
                IRabbitRepo? rabbitRepo = rabbitRepos.Where(r => r.SystemUrl.ExternalUrl == paymentTransaction.ExternalUrl).FirstOrDefault();
                if (rabbitRepo != null)
                {
                    await rabbitRepo.PublishAsync<PaymentTransaction>("updateUserSubscription", paymentTransaction);
                    logger.LogInformation(" Published event updateUserSubscription for Customer = " + paymentTransaction.UserInfo.CustomerId);
                }
                else
                {
                    logger.LogError($" Error : RabbitRepo for {paymentTransaction.ExternalUrl} can not be found");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(" Error in PublishRepo.UpdateUserSubscriptionAsync. Error was : " + ex.Message);
            }
        }
        public static async Task UpdateUserPingInfosAsync(ILogger logger, List<IRabbitRepo> rabbitRepos, PaymentTransaction paymentTransaction)
        {
            IRabbitRepo? rabbitRepo;
            try
            {
                rabbitRepo = rabbitRepos.Where(r => r.SystemUrl.ExternalUrl == paymentTransaction.ExternalUrl).FirstOrDefault();
                if (rabbitRepo != null)
                {
                    await rabbitRepo.PublishAsync<PaymentTransaction>("updateUserPingInfos", paymentTransaction);
                    logger.LogInformation(" Published event updateUserPingInfos for Customer = " + paymentTransaction.UserInfo.CustomerId);
                }
                else
                {
                    logger.LogError($" Error : RabbitRepo for {paymentTransaction.ExternalUrl} can not be found");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(" Error in PublishRepo.UpdateUserPingInfosAsync. Error was : " + ex.Message);
            }
        }
        public static async Task CreateUserSubscriptionAsync(ILogger logger, List<IRabbitRepo> rabbitRepos, PaymentTransaction paymentTransaction)
        {
            try
            {
                IRabbitRepo? rabbitRepo = rabbitRepos.Where(r => r.SystemUrl.ExternalUrl == paymentTransaction.ExternalUrl).FirstOrDefault();
                if (rabbitRepo != null)
                {
                    await rabbitRepo.PublishAsync<PaymentTransaction>("createUserSubscription", paymentTransaction);
                    logger.LogInformation(" Published event createUserSubscription for User = " + paymentTransaction.UserInfo.UserID);
                }
                else
                {
                    logger.LogError($" Error : RabbitRepo for {paymentTransaction.ExternalUrl} can not be found");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(" Error in PublishRepo.CreateUserSubscriptionAsync. Error was : " + ex.Message);
            }
        }
    }
}