using System;
using System.Collections.Generic;
using System.Linq;
using NetworkMonitor.Objects.ServiceMessage;
using System.Diagnostics;
using MetroLog;
using System.Threading.Tasks;
namespace NetworkMonitor.Objects.Repository
{
    public class PublishRepo
    {
        public static async Task PaymentReadyAsync(ILogger logger, RabbitListener rabbitListener, bool isReady)
        {
            var paymentInitObj = new PaymentServiceInitObj();
            paymentInitObj.IsPaymentServiceReady = isReady;
            await rabbitListener.PublishAsync<PaymentServiceInitObj>("paymentServiceReady", paymentInitObj);
            logger.Info(" Published event PaymentServiceItitObj.IsPaymentServiceReady = " + isReady);
        }
        public static async Task UpdateUserSubscriptionAsync(ILogger logger, RabbitListener rabbitListener, PaymentTransaction paymentTransaction)
        {
            await rabbitListener.PublishAsync<PaymentTransaction>("updateUserSubscription", paymentTransaction);
            logger.Info(" Published event updateUserSubscription for user = " + paymentTransaction.UserInfo.UserID);
        }
        public static async Task CreateUserSubscriptionAsync(ILogger logger, RabbitListener rabbitListener, PaymentTransaction paymentTransaction)
        {
            await rabbitListener.PublishAsync<PaymentTransaction>("createUserSubscription", paymentTransaction);
            logger.Info(" Published event createUserSubscription for user = " + paymentTransaction.UserInfo.UserID);
        }
    }
}