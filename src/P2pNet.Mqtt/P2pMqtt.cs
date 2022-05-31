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

namespace P2pNet
{
    public class P2pMqtt : P2pNetBase
    {
        class JoinContext
        {
            public P2pNetChannelInfo mainChannel;
            public string localPeerId;
            public string localHelloData;
        };

        private JoinContext joinContext;


        private readonly object queueLock = new object();
        private Queue<P2pNetMessage> rcvMessageQueue;

        private readonly MQTTnet.Client.IMqttClient mqttClient;
        private readonly Dictionary<string,string> connectOpts;

        public P2pMqtt(IP2pNetClient _client, string _connectionString) : base(_client, _connectionString)
        {
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


        protected override void CarrierProtocolPoll()
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
                    OnReceivedNetMessage(msg.dstChannel, msg);
                }
            }
        }

        private void ResetJoinVars()
        {
            joinContext = null;
            rcvMessageQueue = new Queue<P2pNetMessage>();
        }

        protected override void CarrierProtocolJoin(P2pNetChannelInfo mainChannel, string localPeerId, string localHelloData)
        {
            logger.Verbose($"MQTT join()");
            ResetJoinVars();

            joinContext = new JoinContext(){mainChannel = mainChannel, localPeerId=localPeerId, localHelloData=localHelloData};

            // TODO: add TLS

            // Create TCP based options using the builder.
            IMqttClientOptions options = new MqttClientOptionsBuilder()
                .WithClientId(localPeerId)
                .WithTcpServer(connectOpts["server"])
                .WithCredentials(connectOpts["user"], connectOpts["pwd"])
                .WithCleanSession() // p2pnet should not persist
                .Build();

            // from here downs runs async, Join() just returns
            mqttClient.ConnectAsync(options, CancellationToken.None);
        }

        protected override void CarrierProtocolLeave()
        {
            // No options, but fails if it's null.
            MqttClientDisconnectOptions options = new MqttClientDisconnectOptions();

            mqttClient.DisconnectAsync(options, CancellationToken.None);

            // TODO: no we need to cancel anything? Or does disconnect cancel pending stuff?

            ResetJoinVars();
        }

        protected override void CarrierProtocolSend(P2pNetMessage msg)
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

        protected override void CarrierProtocolListen(string channel)
        {
            // Subscribe to a topic
            logger.Verbose($"MQTT Listen( {channel} )");
            MqttClientSubscribeOptions options = new MqttClientSubscribeOptionsBuilder().WithTopicFilter(channel).Build();
            mqttClient.SubscribeAsync(options, CancellationToken.None);
        }


        protected override void CarrierProtocolStopListening(string channel)
        {
            logger.Verbose($"MQTT StopListening( {channel} )");
            MqttClientUnsubscribeOptions options = new MqttClientUnsubscribeOptionsBuilder().WithTopicFilter(channel).Build();
            mqttClient.UnsubscribeAsync(options, CancellationToken.None);
        }

        protected override void CarrierProtocolAddReceiptTimestamp(P2pNetMessage msg)
        {
            msg.rcptTime = P2pNetDateTime.NowMs;
        }

        // Async Handlers
        private void _OnClientConnected(MqttClientConnectedEventArgs args)
        {
            logger.Verbose($"MQTT _OnClientConnected()");
             CarrierProtocolListen(joinContext.localPeerId);
             OnNetworkJoined(joinContext.mainChannel, joinContext.localHelloData);
             // OnNetworkJoined needs to potentially queue any reporting to client
        }

        private void _OnClientDisconnected(MqttClientDisconnectedEventArgs args)
        {
            logger.Verbose($"MQTT _OnClientDisconnectConnected(): {args.Reason}");
        }


        private void _OnMsgReceived(MqttApplicationMessageReceivedEventArgs args )
        {
            MqttApplicationMessage mqttMsg = args.ApplicationMessage;
            P2pNetMessage msg = JsonConvert.DeserializeObject<P2pNetMessage>(Encoding.UTF8.GetString(mqttMsg.Payload));
             CarrierProtocolAddReceiptTimestamp(msg);
            lock(queueLock)
                rcvMessageQueue.Enqueue(msg); // queue it up
        }

    }
}
