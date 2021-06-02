using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Subscribing;
using MQTTnet.Client.Options;
using Newtonsoft.Json;

namespace P2pNet
{
    public class P2pMqtt : P2pNetBase
    {
        private readonly object queueLock = new object();
        List<P2pNetMessage> messageQueue;
        MQTTnet.Client.IMqttClient mqttClient;
        Dictionary<string,string> connectOpts;

        public P2pMqtt(IP2pNetClient _client, string _connectionString) : base(_client, _connectionString)
        {
            messageQueue = new List<P2pNetMessage>();

            // {"host":"<hostname>"}
            connectOpts = JsonConvert.DeserializeObject<Dictionary<string,string>>(_connectionString);

            // Create a new MQTT client.
            MqttFactory factory = new MqttFactory();
            mqttClient = factory.CreateMqttClient();
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

        protected override void _Join(P2pNetChannelInfo mainChannel, string localPeerId, string localHelloData)
        {
            // TODO: add TLS

            // Create TCP based options using the builder.
            IMqttClientOptions options = new MqttClientOptionsBuilder()
                .WithClientId(localPeerId)
                .WithTcpServer(connectOpts["server"])
                //.WithCredentials("bud", "%spencer%")
                .WithCleanSession() // p2pnet should not persist
                .Build();

            // from here downs runs async, Join() just returns
            mqttClient.ConnectAsync(options, CancellationToken.None); // Since 3.0.5 with CancellationToken
            mqttClient.UseConnectedHandler( e =>
            {
                mqttClient.UseApplicationMessageReceivedHandler(OnMsgReceived);
                // runs when ConnectAsync is done
                _Listen(localPeerId);
                _OnNetworkJoined(mainChannel, localHelloData);
            });
        }

        protected override void _Leave()
        {
            // FIXME
            throw new NotImplementedException();
        }

        protected override void _Send(P2pNetMessage msg)
        {
            string msgJSON = JsonConvert.SerializeObject(msg);

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(msg.dstChannel)
                .WithPayload(msgJSON)
                .WithExactlyOnceQoS()
                .Build();

             mqttClient.PublishAsync(message, CancellationToken.None); // Since 3.0.5 with CancellationToken
        }

        protected void OnMsgReceived(MqttApplicationMessageReceivedEventArgs args )
        {
            MqttApplicationMessage mqttMsg = args.ApplicationMessage;
            P2pNetMessage msg = JsonConvert.DeserializeObject<P2pNetMessage>(Encoding.UTF8.GetString(mqttMsg.Payload));
             _AddReceiptTimestamp(msg);
            lock(queueLock)
                messageQueue.Add(msg); // queue it up
        }


        protected override void _Listen(string channel)
        {
            // Subscribe to a topic
            MqttClientSubscribeOptions options = new MqttClientSubscribeOptionsBuilder().WithTopicFilter(channel).Build();
            Task.Run( () => { mqttClient.SubscribeAsync(options, CancellationToken.None); });
        }


        protected override void _StopListening(string channel)
        {
            // FIXME
            throw new NotImplementedException();
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
}
