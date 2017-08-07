using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using RabbitMQ.Client;
using RabbitMQ.Client.Framing;

namespace NLog.RabbitMQ.Extension.Targets
{
    [Target("RabbitMQ")]
    public class RabbitMQ : TargetWithLayout
    {
        private IConnection _connection;
        private IModel _model;
        private readonly Encoding _encoding = Encoding.UTF8;
        private readonly Queue<Tuple<byte[], IBasicProperties, string>> _unsentMessages = new Queue<Tuple<byte[], IBasicProperties, string>>();

        private MethodInfo _connectionShutdownEventAddMethod;
        private Delegate _connectionShutdownEventHandler;

        #region  Config Parameters
        public string VHost { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public ushort Port { get; set; }
        public Layout Topic { get; set; }
        public string HostName { get; set; }
        public string Exchange { get; set; }
        public bool Durable { get; set; }
        public bool Passive { get; set; }
        public string AppId { get; set; }
        public int MaxBuffer { get; set; }
        public ushort HeartBeatSeconds { get; set; }
        public bool UseJson { get; set; }
        public bool UseSsl { get; set; }
        public string SslCertPath { get; set; }
        public string SslCertPassphrase { get; set; }
        public DeliveryMode DeliveryMode { get; set; }
        public int Timeout { get; set; }
        public CompressionTypes Compression { get; set; }
        [ArrayParameter(typeof(Field), "field")]
        public IList<Field> Fields { get; private set; }
        public bool UseLayoutAsMessage { get; set; }
        #endregion

        public RabbitMQ()
        {
            Layout.FromString("${message}");
            Compression = CompressionTypes.None;
            Fields = new List<Field>();
            PrepareConnectionShutdownEventHandler();
        }

        protected override void Write(AsyncLogEventInfo logEvent)
        {
            AsyncContinuation continuation = logEvent.Continuation;
            IBasicProperties basicProperties = GetBasicProperties(logEvent);
            byte[] numArray = CompressMessage(GetMessage(logEvent));
            string topic = GetTopic(logEvent.LogEvent);
            if (_model == null || !_model.IsOpen)
                StartConnection();
            if (_model != null)
            {
                if (_model.IsOpen)
                {
                    try
                    {
                        CheckUnsent();
                        Publish(numArray, basicProperties, topic);
                        return;
                    }
                    catch (IOException ex)
                    {
                        AddUnsent(topic, basicProperties, numArray);
                        continuation.Invoke(ex);
                    }
                    catch (ObjectDisposedException ex)
                    {
                        AddUnsent(topic, basicProperties, numArray);
                        continuation.Invoke(ex);
                    }
                    ShutdownAmqp(_connection, new ShutdownEventArgs(0, 504, "Could not talk to RabbitMQ instance"));
                    return;
                }
            }
            AddUnsent(topic, basicProperties, numArray);
        }

        private void AddUnsent(string routingKey, IBasicProperties basicProperties, byte[] message)
        {
            if (_unsentMessages.Count < MaxBuffer)
                _unsentMessages.Enqueue(Tuple.Create(message, basicProperties, routingKey));
            else
                InternalLogger.Warn("MaxBuffer {0} filled. Ignoring message.", new object[]
                {
                    MaxBuffer
                });
        }

        private void CheckUnsent()
        {
            while (_unsentMessages.Count > 0)
            {
                Tuple<byte[], IBasicProperties, string> tuple = _unsentMessages.Dequeue();
                InternalLogger.Info("publishing unsent message: {0}.", new object[]
                {
          tuple
                });
                Publish(tuple.Item1, tuple.Item2, tuple.Item3);
            }
        }

        private void Publish(byte[] bytes, IBasicProperties basicProperties, string routingKey)
        {
            _model.BasicPublish(Exchange, routingKey, true, basicProperties, bytes);
        }

        private string GetTopic(LogEventInfo eventInfo)
        {
            return Topic.Render(eventInfo).Replace("{0}", eventInfo.Level.Name);
        }

        private byte[] GetMessage(AsyncLogEventInfo info)
        {
            return _encoding.GetBytes(MessageFormatter.GetMessageInner(UseJson, UseLayoutAsMessage, Layout, info.LogEvent, Fields));
        }

        private IBasicProperties GetBasicProperties(AsyncLogEventInfo loggingEvent)
        {
            LogEventInfo logEvent = loggingEvent.LogEvent;
            BasicProperties basicProperties = new BasicProperties
            {
                ContentEncoding = "utf8",
                ContentType = (!UseJson ? "text/plain" : "application/json"),
                AppId = AppId ?? logEvent.LoggerName,
                Timestamp = new AmqpTimestamp(MessageFormatter.GetEpochTimeStamp(logEvent)),
                UserId = (UserName),
                DeliveryMode = (byte)DeliveryMode
            };
            return basicProperties;
        }

        protected override void InitializeTarget()
        {
            base.InitializeTarget();
            StartConnection();
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        private void StartConnection()
        {
            if (Task.Factory.StartNew(() =>
            {
                try
                {
                    _connection = GetConnectionFac().CreateConnection();
                    AddConnectionShutdownDelegate(_connection);
                    try
                    {
                        _model = _connection.CreateModel();
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Error("could not create model, {0}", new object[]
                        {
                            ex
                        });
                    }
                    if (_model == null)
                        return;
                    if (Passive)
                        return;
                    try
                    {
                        _model.ExchangeDeclare(Exchange, ExchangeType.Topic, Durable);
                    }
                    catch (Exception ex)
                    {
                        if (_model != null)
                        {
                            _model.Dispose();
                            _model = null;
                        }
                        InternalLogger.Error($"could not declare exchange, {ex}");
                    }
                }
                catch (Exception ex)
                {
                    InternalLogger.Error($"could not connect to Rabbit instance, {ex}");
                }
            }).Wait(TimeSpan.FromMilliseconds(Timeout)))
                return;
            InternalLogger.Warn("starting connection-task timed out, continuing");
        }

        private ConnectionFactory GetConnectionFac()
        {
            ConnectionFactory connectionFactory1 = new ConnectionFactory
            {
                HostName = HostName,
                VirtualHost = VHost,
                UserName = UserName,
                Password = Password,
                RequestedHeartbeat = HeartBeatSeconds,
                Port = Port
            };
            ConnectionFactory connectionFactory2 = connectionFactory1;
            SslOption sslOption1 = new SslOption
            {
                Enabled = UseSsl,
                CertPath = SslCertPath,
                CertPassphrase = SslCertPassphrase,
                ServerName = HostName
            };
            SslOption sslOption2 = sslOption1;
            connectionFactory2.Ssl = sslOption2;
            return connectionFactory1;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        private void ShutdownAmqp(IConnection connection, ShutdownEventArgs reason)
        {
            try
            {
                if (_model != null)
                {
                    if (_model.IsOpen)
                    {
                        if (reason.ReplyCode != 504)
                        {
                            if (reason.ReplyCode != 320)
                                _model.Abort();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error("could not close model, {0}", new object[]
                {
          ex
                });
            }
            try
            {
                if (connection == null || !connection.IsOpen)
                    return;
                AddConnectionShutdownDelegate(connection);
                connection.Close(reason.ReplyCode, reason.ReplyText, 1000);
                connection.Abort(1000);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("could not close connection, {0}", new object[]
                {
          ex
                });
            }
        }

        private void ShutdownAmqp35(object sender, ShutdownEventArgs e)
        {
            ShutdownAmqp((IConnection)sender, e);
        }

        private void PrepareConnectionShutdownEventHandler()
        {
            EventInfo eventInfo = typeof(IConnection).GetEvent("ConnectionShutdown");
            _connectionShutdownEventAddMethod = eventInfo.GetAddMethod();
            Type eventHandlerType = eventInfo.EventHandlerType;
            MethodInfo method = !eventHandlerType.IsGenericType || !(eventHandlerType.GetGenericTypeDefinition() == typeof(EventHandler<>)) ? typeof(RabbitMQ).GetMethod("ShutdownAmqp", BindingFlags.Instance | BindingFlags.NonPublic) : typeof(RabbitMQ).GetMethod("ShutdownAmqp35", BindingFlags.Instance | BindingFlags.NonPublic);
            _connectionShutdownEventHandler = Delegate.CreateDelegate(eventHandlerType, this, method);
        }

        private void AddConnectionShutdownDelegate(IConnection connection)
        {
            _connectionShutdownEventAddMethod.Invoke(connection, new object[]
            {
        _connectionShutdownEventHandler
            });
        }

        protected override void CloseTarget()
        {
            ShutdownAmqp(_connection, new ShutdownEventArgs(0, 200, "closing appender"));
            base.CloseTarget();
        }

        private byte[] CompressMessage(byte[] messageBytes)
        {
            switch (Compression)
            {
                case CompressionTypes.None:
                    return messageBytes;
                case CompressionTypes.GZip:
                    return CompressMessageGZip(messageBytes);
                default:
                    throw new NLogConfigurationException($"Compression type '{Compression}' not supported.");
            }
        }

        private byte[] CompressMessageGZip(byte[] messageBytes)
        {
            MemoryStream memoryStream = new MemoryStream();
            using (GZipStream gzipStream = new GZipStream(memoryStream, CompressionMode.Compress))
                gzipStream.Write(messageBytes, 0, messageBytes.Length);
            return memoryStream.ToArray();
        }

        public enum CompressionTypes
        {
            None,
            GZip,
        }
    }
}
