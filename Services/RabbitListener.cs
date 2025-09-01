using RabbitMQ.Client.Events;
using RabbitMQ.Client;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Utils.Helpers;
using NetworkMonitor.Objects.Factory;
namespace NetworkMonitor.Payment.Services
{
    public interface IRabbitListener
    {
        Task<ResultObj> WakeUp();
        Task<ResultObj> PaymentCheck();
        Task<ResultObj> PaymentComplete(PaymentTransaction paymentTransaction);
        Task<ResultObj> RegisterUser(RegisteredUser registeredUser);
        Task Shutdown();
        Task<ResultObj> Setup();
        Task<ResultObj> UpdateProducts(UpdateProductObj update);
    }


    public class RabbitListener : RabbitListenerBase, IRabbitListener
    {

        private IStripeService _stripeService;
        public RabbitListener(IStripeService stripeService, ILogger<RabbitListenerBase> logger, SystemParams systemParams) : base(logger, DeriveSystemUrl(systemParams))
        {
            _stripeService = stripeService;
        }



        private static SystemUrl DeriveSystemUrl(SystemParams systemParams)
        {
            return systemParams.ThisSystemUrl;
        }
        protected override void InitRabbitMQObjs()
        {
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "paymentWakeUp",
                FuncName = "paymentWakeUp",
                MessageTimeout = 59000
            });
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "pingInfosComplete",
                FuncName = "pingInfosComplete"
            });
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "paymentComplete",
                FuncName = "paymentComplete"
            });
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "paymentCheck",
                FuncName = "paymentCheck",
                MessageTimeout = 59000
            });
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "registerUser",
                FuncName = "registerUser"
            });
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "updateProducts",
                FuncName = "updateProducts"
            });
        }
        protected override async Task<ResultObj> DeclareConsumers()
        {
            var result = new ResultObj();
            try
            {
                await Parallel.ForEachAsync(_rabbitMQObjs, async (rabbitMQObj, cancellationToken) =>
                 {

                     if (rabbitMQObj.ConnectChannel != null)
                     {

                         rabbitMQObj.Consumer = new AsyncEventingBasicConsumer(rabbitMQObj.ConnectChannel);
                         await rabbitMQObj.ConnectChannel.BasicConsumeAsync(
                                 queue: rabbitMQObj.QueueName,
                                 autoAck: false,
                                 consumer: rabbitMQObj.Consumer
                             );

                         switch (rabbitMQObj.FuncName)
                         {
                             case "paymentWakeUp":
                                 await rabbitMQObj.ConnectChannel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false);
                                 rabbitMQObj.Consumer.ReceivedAsync += async (model, ea) =>
                            {
                                try
                                {
                                    result = await WakeUp();
                                    await rabbitMQObj.ConnectChannel.BasicAckAsync(ea.DeliveryTag, false);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(" Error : RabbitListener.DeclareConsumers.paymentWakeUp " + ex.Message);
                                }
                            };
                                 break;
                             case "paymentComplete":
                                 await rabbitMQObj.ConnectChannel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false);
                                 rabbitMQObj.Consumer.ReceivedAsync += async (model, ea) =>
                            {
                                try
                                {
                                    result = await PaymentComplete(ConvertToObject<PaymentTransaction>(model, ea));
                                    await rabbitMQObj.ConnectChannel.BasicAckAsync(ea.DeliveryTag, false);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(" Error : RabbitListener.DeclareConsumers.paymentComplete " + ex.Message);
                                }
                            };
                                 break;
                             case "pingInfosComplete":
                                 await rabbitMQObj.ConnectChannel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false);
                                 rabbitMQObj.Consumer.ReceivedAsync += async (model, ea) =>
                            {
                                try
                                {
                                    result = await PingInfosComplete(ConvertToObject<PaymentTransaction>(model, ea));
                                    await rabbitMQObj.ConnectChannel.BasicAckAsync(ea.DeliveryTag, false);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(" Error : RabbitListener.DeclareConsumers.pingInfosComplete " + ex.Message);
                                }
                            };
                                 break;
                             case "paymentCheck":
                                 await rabbitMQObj.ConnectChannel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false);
                                 rabbitMQObj.Consumer.ReceivedAsync += async (model, ea) =>
                            {
                                try
                                {
                                    result = await PaymentCheck();
                                    await rabbitMQObj.ConnectChannel.BasicAckAsync(ea.DeliveryTag, false);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(" Error : RabbitListener.DeclareConsumers.paymentCheck " + ex.Message);
                                }
                            };
                                 break;
                             case "registerUser":
                                 await rabbitMQObj.ConnectChannel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false);
                                 rabbitMQObj.Consumer.ReceivedAsync += async (model, ea) =>
                            {
                                try
                                {
                                    result = await RegisterUser(ConvertToObject<RegisteredUser>(model, ea));
                                    await rabbitMQObj.ConnectChannel.BasicAckAsync(ea.DeliveryTag, false);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(" Error : RabbitListener.DeclareConsumers.registerUser " + ex.Message);
                                }
                            };
                                 break;
                             case "updateProducts":
                                 await rabbitMQObj.ConnectChannel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false);
                                 rabbitMQObj.Consumer.ReceivedAsync += async (model, ea) =>
                                 {
                                     try
                                     {
                                         var payload = ConvertToObject<UpdateProductObj>(model, ea);
                                         var r = await UpdateProducts(payload!);
                                         _logger.LogInformation(r.Message);
                                         await rabbitMQObj.ConnectChannel.BasicAckAsync(ea.DeliveryTag, false);
                                     }
                                     catch (Exception ex)
                                     {
                                         _logger.LogError(" Error : RabbitListener.DeclareConsumers.updateProducts " + ex.Message);
                                     }
                                 };
                                 break;

                         }
                     }
                 });
                result.Success = true;
                result.Message += " Success : Declared all consumers ";
            }
            catch (Exception e)
            {
                string message = " Error : failed to declate consumers. Error was : " + e.ToString() + " . ";
                result.Message += message;
                Console.WriteLine(result.Message);
                result.Success = false;
            }
            return result;
        }
        public async Task<ResultObj> WakeUp()
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : WakeUp : ";
            try
            {
                result = await _stripeService.WakeUp();
                _logger.LogInformation(result.Message);
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                _logger.LogError(result.Message);
            }
            return result;
        }
        public async Task<ResultObj> PaymentCheck()
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : PaymentCheck : ";
            try
            {
                result = await _stripeService.PaymentCheck();
                _logger.LogInformation(result.Message);
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                _logger.LogError(result.Message);
            }
            return result;
        }
        public async Task<ResultObj> PaymentComplete(PaymentTransaction? paymentTransaction)
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : Payment Complete : ";
            if (paymentTransaction == null)
            {
                result.Message += " paymentTransaction is null";
                return result;
            }
            try
            {
                result = await _stripeService.PaymentComplete(paymentTransaction);
                _logger.LogInformation(result.Message);
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                _logger.LogError(result.Message);
            }
            return result;
        }

        public async Task<ResultObj> PingInfosComplete(PaymentTransaction? paymentTransaction)
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : PingInfos Complete : ";
            if (paymentTransaction == null)
            {
                result.Message += " paymentTransaction is null";
                return result;
            }
            try
            {
                result = await _stripeService.PingInfosComplete(paymentTransaction);
                _logger.LogInformation(result.Message);
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                _logger.LogError(result.Message);
            }
            return result;
        }

        public async Task<ResultObj> RegisterUser(RegisteredUser? registeredUser)
        {
            var result = new ResultObj();
            if (registeredUser == null)
            {
                result.Message += " registeredUser is null";
                return result;
            }
            try
            {
                result = await _stripeService.RegisterUser(registeredUser);
                _logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                string message = " Failed to Register User. Eror was : " + ex.Message;
                _logger.LogError(message);
                result.Success = false;
                result.Message = message;
            }
            return result;
        }

        public async Task<ResultObj> UpdateProducts(UpdateProductObj? update)
        {
            var result = new ResultObj { Success = false, Message = "MessageAPI : UpdateProducts : " };
            if (update == null)
            {
                result.Message += " payload is null";
                return result;
            }

            try
            {
                // delegate to StripeService
                result = await _stripeService.UpdateProducts(update);
                _logger.LogInformation(result.Message);
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Message += "Error : Failed to process updateProducts : " + e.Message + " ";
                _logger.LogError(result.Message);
            }
            return result;
        }

    }
}
