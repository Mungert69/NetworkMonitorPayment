using System;
using System.Collections.Generic;
using System.Linq;
using NetworkMonitor.Objects.ServiceMessage;
using Newtonsoft.Json;
using MetroLog;
using System.Threading.Tasks;
namespace NetworkMonitor.Objects.Repository
{
    public class PublishRepo
    {
        public static async Task UpdateProductsAsync(ILogger logger, List<RabbitListener> rabbitListeners, List<ProductObj> products)
        {

            var updateProductObj=new UpdateProductObj(){Products = products};
             logger.Info(" Publishing products : " + JsonConvert.SerializeObject(updateProductObj));
            
            // publish to all systems.
            foreach (RabbitListener rabbitListener in rabbitListeners)
            {
                await rabbitListener.PublishAsync<UpdateProductObj>("updateProducts", updateProductObj);
                logger.Info(" Published event updateProducts to: " + rabbitListener.SystemUrl);
            }
        }
        public static async Task PaymentReadyAsync(ILogger logger, List<RabbitListener> rabbitListeners, bool isReady)
        {
            var paymentInitObj = new PaymentServiceInitObj();
            paymentInitObj.IsPaymentServiceReady = isReady;
            // publish to all systems.
            foreach (RabbitListener rabbitListener in rabbitListeners)
            {
                await rabbitListener.PublishAsync<PaymentServiceInitObj>("paymentServiceReady", paymentInitObj);
                logger.Info(" Published event PaymentServiceItitObj.IsPaymentServiceReady = " + isReady);
            }
        }
        public static async Task UpdateUserSubscriptionAsync(ILogger logger, List<RabbitListener> rabbitListeners, PaymentTransaction paymentTransaction)
        {
            try{
                 RabbitListener rabbitListener = rabbitListeners.Where(r => r.SystemUrl.ExternalUrl == paymentTransaction.ExternalUrl).FirstOrDefault();
            await rabbitListener.PublishAsync<PaymentTransaction>("updateUserSubscription", paymentTransaction);
            logger.Info(" Published event updateUserSubscription for Customer = " + paymentTransaction.UserInfo.CustomerId);
      
            }
            catch(Exception ex){
                logger.Error(" Error in PublishRepo.UpdateUserSubscriptionAsync. Error was : " + ex.Message);
            }
             }
        public static async Task CreateUserSubscriptionAsync(ILogger logger, List<RabbitListener> rabbitListeners, PaymentTransaction paymentTransaction)
        {
            try{
                RabbitListener rabbitListener = rabbitListeners.Where(r => r.SystemUrl.ExternalUrl == paymentTransaction.ExternalUrl).FirstOrDefault();
            await rabbitListener.PublishAsync<PaymentTransaction>("createUserSubscription", paymentTransaction);
            logger.Info(" Published event createUserSubscription for User = " + paymentTransaction.UserInfo.UserID);
      
            }
            catch(Exception ex){
                logger.Error(" Error in PublishRepo.CreateUserSubscriptionAsync. Error was : " + ex.Message);
            }
              }
    }
}