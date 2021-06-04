using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;
using System.Collections.Concurrent;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Subscribing;
using MQTTnet.Client.Options;
using Newtonsoft.Json;

namespace P2pNet
{
    // MQTTnet only has async methods. Mostly this is ok, but the async send wants for you to `await` it - which involves waiting
    // for the ack from the server. If you don't await - and instead just fire-and-forget - you sometines end up with out-of-order
    // messages - which is supposed to be part of what the MQTT protocol provides at all QOS levels.

    // SO anyway - this means that application programmers have to deal with message ordering/trhead management.
    //THat kinda - well - isn't how I think it oughta be. :-)

    // I *think* it's the send, and not receive... let's see


    public class P2pMqtt : P2pNetBase
    {
        private readonly object queueLock = new object();
        private Queue<P2pNetMessage> rcvMessageQueue;

        private ConcurrentQueue<MqttApplicationMessage> sendMessageQueue;
        private ManualResetEventSlim sendQueueReset;

        private MQTTnet.Client.IMqttClient mqttClient;
        private Dictionary<string,string> connectOpts;

        public P2pMqtt(IP2pNetClient _client, string _connectionString) : base(_client, _connectionString)
        {
            rcvMessageQueue = new Queue<P2pNetMessage>();
            sendMessageQueue = new ConcurrentQueue<MqttApplicationMessage>(); // unbounded
            sendQueueReset = new ManualResetEventSlim(false);

            // {"host":"<hostname>"}
            connectOpts = JsonConvert.DeserializeObject<Dictionary<string,string>>(_connectionString);

            // Create a new MQTT client.
            MqttFactory factory = new MqttFactory();
            mqttClient = factory.CreateMqttClient();
        }


        protected override void _Poll()
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

                Task.Run( async () =>
                {
                    while (true)
                    {
                        sendQueueReset.Wait();
                        if (sendMessageQueue == null) // to exit set sendMessageQueue to null and set the reset event
                            break;

                        MqttApplicationMessage msg = null;
                        while (sendMessageQueue.TryDequeue(out msg))
                        {
                            await mqttClient.PublishAsync(msg, CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                });


                // runs when ConnectAsync is done
                _Listen(localPeerId);
                _OnNetworkJoined(mainChannel, localHelloData);
            });
        }

        protected override void _Leave()
        {
            sendMessageQueue = null;
            sendQueueReset.Set(); // finishes the publishing task
            // FIXME: need to do more than this
        }

        protected override void _Send(P2pNetMessage msg)
        {
            // We want this to be fire-n-forget for the caller, so we just do the syncronous
            // message construction and queue up the result.
            string msgJSON = JsonConvert.SerializeObject(msg);

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(msg.dstChannel)
                .WithPayload(msgJSON)
                .WithExactlyOnceQoS()
                .Build();

            sendMessageQueue.Enqueue(message); // BlockingCollection default is a ConcurrentQueue
            sendQueueReset.Set();

            // Another thread needs to grab from the queue and the await publish() in order to keep the
            // messages in order.
            //mqttClient.PublishAsync(message, CancellationToken.None); // Since 3.0.5 with CancellationToken
        }

        protected void OnMsgReceived(MqttApplicationMessageReceivedEventArgs args )
        {
            MqttApplicationMessage mqttMsg = args.ApplicationMessage;
            P2pNetMessage msg = JsonConvert.DeserializeObject<P2pNetMessage>(Encoding.UTF8.GetString(mqttMsg.Payload));
             _AddReceiptTimestamp(msg);
            lock(queueLock)
                rcvMessageQueue.Enqueue(msg); // queue it up
        }


        protected override void _Listen(string channel)
        {
            // Subscribe to a topic
            MqttClientSubscribeOptions options = new MqttClientSubscribeOptionsBuilder().WithTopicFilter(channel).Build();
            mqttClient.SubscribeAsync(options, CancellationToken.None);
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
