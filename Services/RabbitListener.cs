using RabbitMQ.Client.Events;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Payment.Services;
using System.Threading.Tasks;
using System;
using MetroLog;
namespace NetworkMonitor.Objects.Repository
{
    public class RabbitListener : RabbitListenerBase
    {
        private IStripeService _stripeService;
        public RabbitListener(ILogger logger,SystemUrl systemUrl, IStripeService stripeService): base(logger, systemUrl)
        {
            _stripeService = stripeService;
	    Setup();
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
                            _logger.Error(" Error : RabbitListener.DeclareConsumers.paymentWakeUp " + ex.Message);
                        }
                    };
                        break;
                    case "paymentComplete":
                        rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                        rabbitMQObj.Consumer.Received += (model, ea) =>
                    {
                        try {
                             result = PaymentComplete(ConvertToObject<PaymentTransaction>(model, ea));
                        rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(" Error : RabbitListener.DeclareConsumers.paymentComplete " + ex.Message);
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
                            _logger.Error(" Error : RabbitListener.DeclareConsumers.paymentCheck " + ex.Message);
                        }
                    };
                        break;
                    case "registerUser":
                        rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                        rabbitMQObj.Consumer.Received +=  (model, ea) =>
                    {
                        try {
                              result = RegisterUser(ConvertToObject<RegisteredUser>(model, ea));
                        rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(" Error : RabbitListener.DeclareConsumers.registerUser " + ex.Message);
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
                _logger.Info(result.Message);
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                _logger.Error(result.Message);
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
                _logger.Info(result.Message);
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                _logger.Error(result.Message);
            }
            return result;
        }
        public ResultObj PaymentComplete(PaymentTransaction paymentTransaction)
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : Payment Complete : ";
            try
            {
                result = _stripeService.PaymentComplete(paymentTransaction);
                _logger.Info(result.Message);
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                _logger.Error(result.Message);
            }
            return result;
        }
           public ResultObj RegisterUser(RegisteredUser RegisteredUser)
        {
            var result =new ResultObj();
            try
            {
                result = _stripeService.RegisterUser(RegisteredUser);
               _logger.Info(result.Message);
            }
            catch (Exception ex)
            {
                string message=" Failed to Register User. Eror was : " + ex.Message;
                _logger.Error(message );
                result.Success = false;
                result.Message = message;
            }
            return result;
        }
     }
}
