using System;
using System.Collections.Generic;
using System.Linq;
using NetworkMonitor.Objects.ServiceMessage;
using System.Diagnostics;
using MetroLog;
using System.Threading;
namespace NetworkMonitor.Objects.Repository
{
    public class PublishRepo
    {
        public static void PaymentReady(ILogger logger, RabbitListener rabbitListener, bool isReady)
        {
            var paymentInitObj = new PaymentServiceInitObj();
            paymentInitObj.IsPaymentServiceReady = isReady;
            rabbitListener.Publish<PaymentServiceInitObj>("paymentServiceReady", paymentInitObj);
            logger.Info(" Published event PaymentServiceItitObj.IsPaymentServiceReady = " + isReady);
        }
        public static void UpdateUserSubscription(ILogger logger, RabbitListener rabbitListener, PaymentTransaction paymentTransaction)
        {
            rabbitListener.Publish<PaymentTransaction>("updateUserSubscription", paymentTransaction);
            logger.Info(" Published event updateUserSubscription for user = " + paymentTransaction.UserInfo.UserID);
        }
        public static void CreateUserSubscription(ILogger logger, RabbitListener rabbitListener, PaymentTransaction paymentTransaction)
        {
            rabbitListener.Publish<PaymentTransaction>("createUserSubscription", paymentTransaction);
            logger.Info(" Published event createUserSubscription for user = " + paymentTransaction.UserInfo.UserID);
        }
    }
}