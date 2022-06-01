using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;
using System.Collections.Concurrent;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Connecting;
using MQTTnet.Client.Disconnecting;
using MQTTnet.Client.Subscribing;
using MQTTnet.Client.Unsubscribing;
using MQTTnet.Client.Options;
using Newtonsoft.Json;
using UniLog;

namespace P2pNet
{
    public class P2pMqtt : IP2pNetCarrier
    {
        class JoinContext
        {
            public IP2pNetBase p2pBase;
            public P2pNetChannelInfo mainChannel;
            public string localHelloData;
        };

        private JoinContext joinContext;

        private readonly object queueLock = new object();
        private Queue<P2pNetMessage> rcvMessageQueue;

        private readonly IMqttClient mqttClient;
        private readonly Dictionary<string,string> connectOpts;
        public UniLogger logger;

        public P2pMqtt(string _connectionString)
        {
            logger = UniLogger.GetLogger("P2pNet");
            logger.Verbose($"MQTT ctor");
            ResetJoinVars();

            // {  "host":"<hostname>"
            //    "user":<user>
            //    "pwd":<pwd>
            // }
            connectOpts = JsonConvert.DeserializeObject<Dictionary<string,string>>(_connectionString);

            // Create a new MQTT client.
            // TODO: This should be in joinand these reset in ResetJoinVars()
            // TODO: this whole implmentation is not done
            MqttFactory factory = new MqttFactory();

            mqttClient = factory.CreateMqttClient();
            mqttClient.UseConnectedHandler( _OnClientConnected);
            mqttClient.UseDisconnectedHandler( _OnClientDisconnected);
            mqttClient.UseApplicationMessageReceivedHandler(_OnMsgReceived);
        }


        private void ResetJoinVars()
        {
            joinContext = null;
            rcvMessageQueue = new Queue<P2pNetMessage>();
        }

        public void Join(P2pNetChannelInfo mainChannel, IP2pNetBase p2pBase, string localHelloData)
        {
            logger.Verbose($"MQTT join()");
            ResetJoinVars();

            joinContext = new JoinContext(){p2pBase=p2pBase, mainChannel=mainChannel, localHelloData=localHelloData};

            // TODO: add TLS

            // Create TCP based options using the builder.
            IMqttClientOptions options = new MqttClientOptionsBuilder()
                .WithClientId(p2pBase.GetId())
                .WithTcpServer(connectOpts["server"])
                .WithCredentials(connectOpts["user"], connectOpts["pwd"])
                .WithCleanSession() // p2pnet should not persist
                .Build();

            // from here downs runs async, Join() just returns
            mqttClient.ConnectAsync(options, CancellationToken.None);
        }

        public void Leave()
        {
            // No options, but fails if it's null.
            MqttClientDisconnectOptions options = new MqttClientDisconnectOptions();

            mqttClient.DisconnectAsync(options, CancellationToken.None);

            // TODO: no we need to cancel anything? Or does disconnect cancel pending stuff?

            ResetJoinVars();
        }

        public void Send(P2pNetMessage msg)
        {
            logger.Verbose($"MQTT send()");
            // We want this to be fire-n-forget for the caller, so we just do the syncronous
            // message construction and queue up the result.
            string msgJSON = JsonConvert.SerializeObject(msg);

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(msg.dstChannel)
                .WithPayload(msgJSON)
                .WithAtMostOnceQoS()
                .WithRetainFlag(false)
                .Build();

            // TODO: Be aware of potential ordering problems with async sending?

            mqttClient.PublishAsync(message, CancellationToken.None);
        }

        public void Listen(string channel)
        {
            // Subscribe to a topic
            logger.Verbose($"MQTT Listen( {channel} )");
            MqttClientSubscribeOptions options = new MqttClientSubscribeOptionsBuilder().WithTopicFilter(channel).Build();
            mqttClient.SubscribeAsync(options, CancellationToken.None);
        }


        public void StopListening(string channel)
        {
            logger.Verbose($"MQTT StopListening( {channel} )");
            MqttClientUnsubscribeOptions options = new MqttClientUnsubscribeOptionsBuilder().WithTopicFilter(channel).Build();
            mqttClient.UnsubscribeAsync(options, CancellationToken.None);
        }

        protected void AddReceiptTimestamp(P2pNetMessage msg)
        {
            msg.rcptTime = P2pNetDateTime.NowMs;
        }

        // Async Handlers
        private void _OnClientConnected(MqttClientConnectedEventArgs args)
        {
            logger.Verbose($"MQTT _OnClientConnected()");
             Listen(joinContext.p2pBase.GetId());
             joinContext.p2pBase.OnNetworkJoined(joinContext.mainChannel, joinContext.localHelloData);
             // OnNetworkJoined needs to potentially queue any reporting to client
        }

        private void _OnClientDisconnected(MqttClientDisconnectedEventArgs args)
        {
            logger.Verbose($"MQTT _OnClientDisconnectConnected(): {args.Reason}");
        }

        public void Poll()
        {
            // receive polling
            if (rcvMessageQueue.Count > 0)
            {
                Queue<P2pNetMessage> prevMessageQueue;
                lock(queueLock)
                {
                    prevMessageQueue = rcvMessageQueue;
                    rcvMessageQueue = new Queue<P2pNetMessage>();
                }

                foreach( P2pNetMessage msg in prevMessageQueue)
                {
                    joinContext.p2pBase.OnReceivedNetMessage(msg.dstChannel, msg);
                }
            }
        }

        private void _OnMsgReceived(MqttApplicationMessageReceivedEventArgs args )
        {
            MqttApplicationMessage mqttMsg = args.ApplicationMessage;
            P2pNetMessage msg = JsonConvert.DeserializeObject<P2pNetMessage>(Encoding.UTF8.GetString(mqttMsg.Payload));
            AddReceiptTimestamp(msg);

            lock(queueLock)
                rcvMessageQueue.Enqueue(msg); // queue it up
        }

    }
}
