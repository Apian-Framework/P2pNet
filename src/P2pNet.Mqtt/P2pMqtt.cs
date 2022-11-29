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
        class JoinState
        {
            public SynchronizationContext mainSyncCtx; // might be null
            public IP2pNetBase p2pBase;
            public P2pNetChannelInfo mainChannel;
            public string localHelloData;
        };

        private JoinState joinState;

        private readonly IMqttClient mqttClient;
        private readonly Dictionary<string,string> connectOpts;
        public UniLogger logger;

        public P2pMqtt(string _connectionString)
        {
            logger = UniLogger.GetLogger("P2pNet");
            logger.Verbose($"MQTT ctor (thread: {Environment.CurrentManagedThreadId})");
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
            joinState = null;
        }

        public void Join(P2pNetChannelInfo mainChannel, IP2pNetBase p2pBase, string localHelloData)
        {
            logger.Verbose($"MQTT join() (thread: {Environment.CurrentManagedThreadId})");
            ResetJoinVars();

            joinState = new JoinState()
            {
                p2pBase=p2pBase,
                mainChannel=mainChannel,
                localHelloData=localHelloData,
                mainSyncCtx = SynchronizationContext.Current
            };

            // TODO: add TLS

            // Create TCP based options using the builder.
            IMqttClientOptions options = new MqttClientOptionsBuilder()
                .WithClientId(p2pBase.LocalId)
                .WithTcpServer(connectOpts["server"])
                .WithCredentials(connectOpts["user"], connectOpts["pwd"])
                .WithCleanSession() // p2pnet should not persist
                .Build();

             mqttClient.ConnectAsync(options, CancellationToken.None).Wait(); // must Wait() in order for calling code to catch exceptions

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
            logger.Debug($"MQTT send() (thread: {Environment.CurrentManagedThreadId})");
            // We want this to be fire-n-forget for the caller, so we just do the syncronous
            // message construction and queue up the result.
            string msgJSON = JsonConvert.SerializeObject(msg);

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(msg.dstChannel)
                .WithPayload(msgJSON)
                .WithAtMostOnceQoS()
                .WithRetainFlag(false)
                .Build();

            mqttClient.PublishAsync(message, CancellationToken.None);
        }

        public void Listen(string channel)
        {
            // Subscribe to a topic
            logger.Verbose($"MQTT Listen( {channel} ) thread: {Environment.CurrentManagedThreadId}");
            MqttClientSubscribeOptions options = new MqttClientSubscribeOptionsBuilder().WithTopicFilter(channel).Build();
            mqttClient.SubscribeAsync(options, CancellationToken.None);
        }


        public void StopListening(string channel)
        {
            logger.Verbose($"MQTT StopListening( {channel} ) thread: {Environment.CurrentManagedThreadId}");
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
            logger.Verbose($"MQTT _OnClientConnected() thread: {Environment.CurrentManagedThreadId}");
             Listen(joinState.p2pBase.LocalId);

            // OnNetworkJoined needs to be synchronized
            if (joinState.mainSyncCtx != null)
            {
                joinState.mainSyncCtx.Post( new SendOrPostCallback( (o) => {
                    joinState.p2pBase.OnNetworkJoined(joinState.mainChannel, joinState.localHelloData);
                } ), null);
            } else {
                joinState.p2pBase.OnNetworkJoined(joinState.mainChannel, joinState.localHelloData);
            }

        }

        private void _OnClientDisconnected(MqttClientDisconnectedEventArgs args)
        {
            logger.Info($"MQTT _OnClientDisconnected(): {args.Reason}");
        }

        public void Poll() {}

        private void _OnMsgReceived(MqttApplicationMessageReceivedEventArgs args )
        {
            logger.Debug($"MQTT _OnMsgReceived() thread: {Environment.CurrentManagedThreadId}");
            MqttApplicationMessage mqttMsg = args.ApplicationMessage;
            P2pNetMessage msg = JsonConvert.DeserializeObject<P2pNetMessage>(Encoding.UTF8.GetString(mqttMsg.Payload));
            AddReceiptTimestamp(msg);

            if (joinState.mainSyncCtx != null)
            {
                joinState.mainSyncCtx.Post( new SendOrPostCallback( (o) => {
                    logger.Debug($"XXXXXXXX thread: {Environment.CurrentManagedThreadId}");
                    joinState.p2pBase.OnReceivedNetMessage(msg.dstChannel, msg);
                } ), null);
            } else {
                joinState.p2pBase.OnReceivedNetMessage(msg.dstChannel, msg);
            }

        }

    }
}
