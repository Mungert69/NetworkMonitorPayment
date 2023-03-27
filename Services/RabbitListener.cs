using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using CloudNative.CloudEvents;
using CloudNative.CloudEvents.NewtonsoftJson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Objects;
using NetworkMonitor.Payment.Services;
using System.Collections.Generic;
using System;
using System.Text;
using System.Linq;
using NetworkMonitor.Utils;
using MetroLog;
namespace NetworkMonitor.Objects.Repository
{
    public class RabbitListener
    {
        private string _instanceName;
        private IModel _publishChannel;
        private ILogger _logger;
        private IStripeService _stripeService;
        private ConnectionFactory _factory;
        private IConnection _connection;
        List<RabbitMQObj> _rabbitMQObjs = new List<RabbitMQObj>();
        public RabbitListener(ILogger logger, IStripeService stripeService, string instanceName, string hostname)
        {
            _logger = logger;
            _stripeService = stripeService;
            _instanceName = instanceName;
            _factory = new ConnectionFactory
            {
                HostName = hostname,
                UserName = "usercommonxf1",
                Password = "test12",
                VirtualHost = "/vhostuser",
                AutomaticRecoveryEnabled = true,
                Port = 5672
            };
            init();
        }
        public void init()
        {
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "paymentWakeUp",
                FuncName = "paymentWakeUp",
                MessageTimeout=60000
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
                MessageTimeout=60000
            });
            _connection = _factory.CreateConnection();
            _publishChannel = _connection.CreateModel();
            _rabbitMQObjs.ForEach(r => r.ConnectChannel = _connection.CreateModel());
            var results = new List<ResultObj>();
            results.Add(DeclareQueues());
            results.Add(DeclareConsumers());
            results.Add(BindChannelToConsumer());
            bool flag = true;
            string messages = "";
            results.ForEach(f => messages += f.Message);
            results.ForEach(f => flag = f.Success && flag);
            if (flag) _logger.Info("Success : Setup RabbitListener messages were : " + messages);
            else _logger.Fatal("Error : Failed to setup RabbitListener messages were : " + messages);
        }
        private ResultObj DeclareQueues()
        {
            var result = new ResultObj();
            result.Message = " RabbitRepo DeclareQueues : ";
            try
            {
                _rabbitMQObjs.ForEach(rabbitMQObj =>
                    {
                        var args = new Dictionary<string, object>();
                        if (rabbitMQObj.MessageTimeout!=0){
                            args.Add("x-message-ttl", rabbitMQObj.MessageTimeout);
                        }
                        else args=null;
                        rabbitMQObj.QueueName = _instanceName + "-" + rabbitMQObj.ExchangeName;
                        rabbitMQObj.ConnectChannel.ExchangeDeclare(exchange: rabbitMQObj.ExchangeName, type: ExchangeType.Fanout, durable: true);
                        rabbitMQObj.ConnectChannel.QueueDeclare(queue: rabbitMQObj.QueueName,
                                             durable: true,
                                             exclusive: false,
                                             autoDelete: true,
                                             arguments: args
                                             );
                        rabbitMQObj.ConnectChannel.QueueBind(queue: rabbitMQObj.QueueName,
                                          exchange: rabbitMQObj.ExchangeName,
                                          routingKey: string.Empty);
                    });
                result.Success = true;
                result.Message += " Success : Declared all queues ";
            }
            catch (Exception e)
            {
                string message = " Error : failed to declate queues. Error was : " + e.ToString() + " . ";
                result.Message += message;
                Console.WriteLine(result.Message);
                result.Success = false;
            }
            return result;
        }
        private ResultObj DeclareConsumers()
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
                        rabbitMQObj.Consumer.Received += (model, ea) =>
                    {
                        result = WakeUp();
                        rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                    };
                        break;
                    case "paymentComplete":
                        rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                        rabbitMQObj.Consumer.Received += (model, ea) =>
                    {
                        result = PaymentComplete(ConvertToObject<PaymentTransaction>(model, ea));
                        rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                    };
                        break;
                    case "paymentCheck":
                        rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                        rabbitMQObj.Consumer.Received += (model, ea) =>
                    {
                        result = PaymentCheck();
                        rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
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
        private ResultObj BindChannelToConsumer()
        {
            var result = new ResultObj();
            result.Message = " RabbitRepo BindChannelToConsumer : ";
            try
            {
                _rabbitMQObjs.ForEach(rabbitMQObj =>
                    {
                        rabbitMQObj.ConnectChannel.BasicConsume(queue: rabbitMQObj.QueueName,
                            autoAck: false,
                            consumer: rabbitMQObj.Consumer
                            );
                    });
                result.Success = true;
                result.Message += " Success :  bound all consumers to queues ";
            }
            catch (Exception e)
            {
                string message = " Error : failed to bind all consumers to queues. Error was : " + e.ToString() + " . ";
                result.Message += message;
                Console.WriteLine(result.Message);
                result.Success = false;
            }
            return result;
        }
        private T ConvertToObject<T>(object sender, BasicDeliverEventArgs @event) where T : class
        {
            T result = null;
            try
            {
                string json = Encoding.UTF8.GetString(@event.Body.ToArray(), 0, @event.Body.ToArray().Length);
                var cloudEvent = JsonConvert.DeserializeObject<CloudEvent>(json);
                JObject dataAsJObject = (JObject)cloudEvent.Data;
                result = dataAsJObject.ToObject<T>();
            }
            catch (Exception e)
            {
                _logger.Error("Error : Unable to convert Object. Error was : " + e.ToString());
            }
            return result;
        }
        private T ConvertToList<T>(object sender, BasicDeliverEventArgs @event) where T : class
        {
            T result = null;
            try
            {
                string json = Encoding.UTF8.GetString(@event.Body.ToArray(), 0, @event.Body.ToArray().Length);
                var cloudEvent = JsonConvert.DeserializeObject<CloudEvent>(json);
                JArray dataAsJObject = (JArray)cloudEvent.Data;
                result = dataAsJObject.ToObject<T>();
            }
            catch (Exception e)
            {
                _logger.Error("Error : Unable to convert Object. Error was : " + e.ToString());
            }
            return result;
        }
        public ResultObj WakeUp()
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : WakeUp : ";
            try
            {
                result = _stripeService.WakeUp();
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
        public ResultObj PaymentCheck()
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : PaymentCheck : ";
            try
            {
                result = _stripeService.PaymentCheck();
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
        public string PublishJsonZ<T>(string exchangeName, T obj) where T : class
        {
            var datajson = JsonUtils.writeJsonObjectToString<T>(obj);
            string datajsonZ = StringCompressor.Compress(datajson);
            CloudEvent cloudEvent = new CloudEvent
            {
                Id = "event-id",
                Type = "event-type",
                Source = new Uri("https://srv1.mahadeva.co.uk"),
                Time = DateTimeOffset.UtcNow,
                Data = datajsonZ
            };
            var formatter = new JsonEventFormatter();
            var json = formatter.ConvertToJObject(cloudEvent);
            string message = json.ToString();
            var body = Encoding.UTF8.GetBytes(message);
            _publishChannel.BasicPublish(exchange: exchangeName,
                                 routingKey: string.Empty,
                                 basicProperties: null,
                                 // body: formatter.EncodeBinaryModeEventData(cloudEvent));
                                 body: body);
            return datajsonZ;
        }
        public void Publish<T>(string exchangeName, T obj) where T : class
        {
            CloudEvent cloudEvent = new CloudEvent
            {
                Id = "event-id",
                Type = "event-type",
                Source = new Uri("https://srv1.mahadeva.co.uk"),
                Time = DateTimeOffset.UtcNow,
                Data = obj
            };
            var formatter = new JsonEventFormatter();
            var json = formatter.ConvertToJObject(cloudEvent);
            string message = json.ToString();
            var body = Encoding.UTF8.GetBytes(message);
            _publishChannel.BasicPublish(exchange: exchangeName,
                                 routingKey: string.Empty,
                                 basicProperties: null,
                                 // body: formatter.EncodeBinaryModeEventData(cloudEvent));
                                 body: body);
        }
        public void Publish(string exchangeName, Object obj)
        {
            CloudEvent cloudEvent = new CloudEvent
            {
                Id = "event-id",
                Type = "event-type",
                Source = new Uri("https://srv1.mahadeva.co.uk"),
                Time = DateTimeOffset.UtcNow,
                Data = obj
            };
            var formatter = new JsonEventFormatter();
            var json = formatter.ConvertToJObject(cloudEvent);
            string message = json.ToString();
            var body = Encoding.UTF8.GetBytes(message);
            _publishChannel.BasicPublish(exchange: exchangeName,
                                 routingKey: string.Empty,
                                 basicProperties: null,
                                 // body: formatter.EncodeBinaryModeEventData(cloudEvent));
                                 body: body);
        }
    }
}