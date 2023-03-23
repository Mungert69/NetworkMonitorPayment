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
        public static void UpdateUserSubscription(ILogger logger,RabbitListener rabbitListener, UserInfo userInfo)
        {
            rabbitListener.Publish<UserInfo>( "updateUserSubscription", userInfo);
            logger.Info(" Published event updateUserSubscription for user = "+userInfo.UserID);
        }
          public static void CreateUserSubscription(ILogger logger,RabbitListener rabbitListener, UserInfo userInfo)
        {
            rabbitListener.Publish<UserInfo>( "createUserSubscription", userInfo);
            logger.Info(" Published event createUserSubscription for user = "+userInfo.UserID);
        }
        public static void PaymentReady(ILogger logger,RabbitListener rabbitListener, bool isReady)
        {

            rabbitListener.Publish( "paymentReady", isReady);
            logger.Info(" Published event paymentReady set isReady = "+isReady);
        }
    }
}