using System;
using System.Collections.Generic;
using MQTTnet;
using MQTTnet.Client.Options;
using Newtonsoft.Json;

namespace P2pNet
{
#if FALSE
    public class P2pMqtt : P2pNetBase
    {
        private readonly object queueLock = new object();
        List<P2pNetMessage> messageQueue;

        MQTTnet.Client.IMqttClient mqttClient;

        public P2pMqtt(IP2pNetClient _client, string _connectionString) : base(_client, _connectionString)
        {
            messageQueue = new List<P2pNetMessage>();

            // Create a new MQTT client.
            MqttFactory factory = new MqttFactory();
            mqttClient = factory.CreateMqttClient();

            // TODO: add TLS

            // Create TCP based options using the builder.
            IMqttClientOptions options = new MqttClientOptionsBuilder()
                .WithClientId("Client1")
                .WithTcpServer("broker.hivemq.com")
                .WithCredentials("bud", "%spencer%")
                .WithCleanSession() // for p2pnet don't persist
                .Build();


        }


        protected override void _Poll()
        {
            if (messageQueue.Count > 0)
            {
                List<P2pNetMessage> prevMessageQueue;
                lock(queueLock)
                {
                    prevMessageQueue = messageQueue;
                    messageQueue = new List<P2pNetMessage>();
                }

                foreach( P2pNetMessage msg in prevMessageQueue)
                {
                    _OnReceivedNetMessage(msg.dstChannel, msg);
                }
            }
        }

        protected override void _Join(P2pNetChannelInfo mainChannel)
        {
            // Doesn't do anything
        }

        protected override void _Leave()
        {
            // reset. Seems heavy handed
            RedisCon.Close();
            RedisCon = ConnectionMultiplexer.Connect(connectionStr);
        }

        protected override bool _Send(P2pNetMessage msg)
        {
            string msgJSON = JsonConvert.SerializeObject(msg);
            RedisCon.GetSubscriber().PublishAsync(msg.dstChannel, msgJSON);
            return true;
        }

        protected override void _Listen(string channel)
        {
            //_ListenConcurrent(channel);
            _ListenSequential(channel);
        }

        protected  void _ListenConcurrent(string channel)
        {
            RedisCon.GetSubscriber().Subscribe(channel, (rcvChannel, msgJSON) => {
                P2pNetMessage msg = JsonConvert.DeserializeObject<P2pNetMessage>(msgJSON);
                _AddReceiptTimestamp(msg);
                lock(queueLock)
                    messageQueue.Add(msg); // queue it up
            });
        }

        protected void _ListenSequential(string channel)
        {
            var rcvChannel = RedisCon.GetSubscriber().Subscribe(channel);

            rcvChannel.OnMessage(channelMsg =>
            {
                P2pNetMessage msg = JsonConvert.DeserializeObject<P2pNetMessage>(channelMsg.Message);
                _AddReceiptTimestamp(msg);
                lock(queueLock)
                    messageQueue.Add(msg); // queue it up
            });
        }

        protected override void _StopListening(string channel)
        {
            RedisCon.GetSubscriber().Unsubscribe(channel);
        }

        protected override string _NewP2pId()
        {
            return System.Guid.NewGuid().ToString();
        }

        protected override void _AddReceiptTimestamp(P2pNetMessage msg)
        {
            msg.rcptTime = P2pNetDateTime.NowMs;
        }

    }
#endif
}
