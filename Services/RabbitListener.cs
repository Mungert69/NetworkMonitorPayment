using RabbitMQ.Client.Events;
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
}

    
    public class RabbitListener : RabbitListenerBase,IRabbitListener
    {

        private IStripeService _stripeService;
        public RabbitListener(IStripeService stripeService, ILogger<RabbitListenerBase> logger, ISystemParamsHelper systemParamsHelper) : base(logger, DeriveSystemUrl(systemParamsHelper))
        {
            _stripeService = stripeService;
	    Setup();
         }

        

        private static SystemUrl DeriveSystemUrl(ISystemParamsHelper systemParamsHelper)
        {
            return systemParamsHelper.GetSystemParams().ThisSystemUrl;
        }
        protected override void InitRabbitMQObjs()
        {
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "paymentWakeUp",
                FuncName = "paymentWakeUp",
                MessageTimeout=59000
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
                MessageTimeout=59000
            });
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "registerUser",
                FuncName = "registerUser"
            });
         }
        protected override ResultObj DeclareConsumers()
        {
            var result = new ResultObj();
            try
            {
                _rabbitMQObjs.ForEach(rabbitMQObj =>
            {
                rabbitMQObj.Consumer = new EventingBasicConsumer(rabbitMQObj.ConnectChannel);
                switch (rabbitMQObj.FuncName)
                {
                    case "paymentWakeUp":
                        rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                        rabbitMQObj.Consumer.Received += async(model, ea) =>
                    {
                        try {
                            result = await WakeUp();
                        rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                        }
                         catch (Exception ex)
                        {
                            _logger.LogError(" Error : RabbitListener.DeclareConsumers.paymentWakeUp " + ex.Message);
                        }
                    };
                        break;
                    case "paymentComplete":
                        rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                        rabbitMQObj.Consumer.Received += async (model, ea) =>
                    {
                        try {
                             result = await PaymentComplete(ConvertToObject<PaymentTransaction>(model, ea));
                        rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(" Error : RabbitListener.DeclareConsumers.paymentComplete " + ex.Message);
                        }
                    };
                        break;
                     case "pingInfosComplete":
                        rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                        rabbitMQObj.Consumer.Received += async (model, ea) =>
                    {
                        try {
                             result = await PingInfosComplete(ConvertToObject<PaymentTransaction>(model, ea));
                        rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(" Error : RabbitListener.DeclareConsumers.pingInfosComplete " + ex.Message);
                        }
                    };
                        break;
                    case "paymentCheck":
                        rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                        rabbitMQObj.Consumer.Received += async (model, ea) =>
                    {
                        try {
                            result = await PaymentCheck();
                        rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(" Error : RabbitListener.DeclareConsumers.paymentCheck " + ex.Message);
                        }
                    };
                        break;
                    case "registerUser":
                        rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                        rabbitMQObj.Consumer.Received +=  async (model, ea) =>
                    {
                        try {
                              result = await RegisterUser(ConvertToObject<RegisteredUser>(model, ea));
                        rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(" Error : RabbitListener.DeclareConsumers.registerUser " + ex.Message);
                        }
                    };
                        break;
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
        public async Task<ResultObj> PaymentComplete(PaymentTransaction paymentTransaction)
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : Payment Complete : ";
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

            public async Task<ResultObj> PingInfosComplete(PaymentTransaction paymentTransaction)
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : PingInfos Complete : ";
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
    
           public async Task<ResultObj> RegisterUser(RegisteredUser RegisteredUser)
        {
            var result =new ResultObj();
            try
            {
                result = await _stripeService.RegisterUser(RegisteredUser);
               _logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                string message=" Failed to Register User. Eror was : " + ex.Message;
                _logger.LogError(message );
                result.Success = false;
                result.Message = message;
            }
            return result;
        }
     }
}
