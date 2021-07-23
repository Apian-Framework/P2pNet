using System;
using System.Collections.Generic;
using StackExchange.Redis;
using Newtonsoft.Json;

namespace P2pNet
{

    public class P2pRedis : P2pNetBase
    {
        private readonly object queueLock = new object();
        private List<P2pNetMessage> messageQueue;
        private IConnectionMultiplexer ConnectionMux {get; set; } = null;
        protected Func<string, IConnectionMultiplexer> customConnectFactory;
        protected string connectionString;

        public P2pRedis(IP2pNetClient _client, string _connectionString, Func<string, IConnectionMultiplexer> _muxConnectFactory=null) : base(_client, _connectionString)
        {
            // valid connection string is typically: "<host>,password=<password>"
            customConnectFactory =  _muxConnectFactory;
            connectionString = _connectionString;
            messageQueue = new List<P2pNetMessage>();
        }

        private string _GuessRedisProblem(string exMsg)
        {
            // RedisConnectionException messages can be pretty long and unhelpful
            // to the user, but the Message property is the only indication what sort
            // of problem has occurred.
            string msg = exMsg;
            if (exMsg.Contains("authentication"))
                msg = "Redis authentication failure";
            if (exMsg.Contains("UnableToConnect on"))
                msg = "Unable to connect to Redis host"; // TODO: parse the bad host out of the message and include it
            return msg;
        }

        protected override void ImplementationPoll()
        {
            if (messageQueue?.Count > 0)
            {
                List<P2pNetMessage> prevMessageQueue;
                lock(queueLock)
                {
                    prevMessageQueue = messageQueue;
                    messageQueue = new List<P2pNetMessage>();
                }

                foreach( P2pNetMessage msg in prevMessageQueue)
                {
                    OnReceivedNetMessage(msg.dstChannel, msg);
                }
            }
        }

        protected override void ImplementationJoin(P2pNetChannelInfo mainChannel, string localPeerId, string localHelloData)
        {

            try {
                ConnectionMux =  customConnectFactory != null ? customConnectFactory(connectionString) : ConnectionMultiplexer.Connect(connectionString); // Use the passed-in test mux instance if supplied
            } catch (StackExchange.Redis.RedisConnectionException ex) {
                logger.Debug(string.Format("P2pRedis Ctor: StackExchange.Redis.RedisConnectionException:{0}", ex.Message));
                throw( new Exception($"{_GuessRedisProblem(ex.Message)}"));
            } catch (System.ArgumentException ex) {
                logger.Debug(string.Format("P2pRedis Ctor: System.ArgumentException:{0}", ex.Message));
                throw( new Exception($"Bad connection string: {ex.Message}"));
            }
            ImplementationListen(localPeerId);
            OnNetworkJoined(mainChannel, localHelloData);
        }

        protected override void ImplementationLeave()
        {
            ConnectionMux.Close();
            ConnectionMux =null;
            customConnectFactory = null;
            connectionString = null;
            messageQueue = null;
        }

        protected override void ImplementationSend(P2pNetMessage msg)
        {
            string msgJSON = JsonConvert.SerializeObject(msg);
            ConnectionMux.GetSubscriber().PublishAsync(msg.dstChannel, msgJSON);
        }

        protected override void ImplementationListen(string channel)
        {
            //_ListenConcurrent(channel);
            _ListenSequential(channel);
        }

        // private void _ListenConcurrent(string channel)
        // {
        //     ConnectionMux.GetSubscriber().Subscribe(channel, (rcvChannel, msgJSON) => {
        //         P2pNetMessage msg = JsonConvert.DeserializeObject<P2pNetMessage>(msgJSON);
        //         ImplementationAddReceiptTimestamp(msg);
        //         lock(queueLock)
        //             messageQueue.Add(msg); // queue it up
        //     });
        // }

        private void _ListenSequential(string channel)
        {
            var rcvChannel = ConnectionMux.GetSubscriber().Subscribe(channel);

            rcvChannel.OnMessage(channelMsg =>
            {
                P2pNetMessage msg = JsonConvert.DeserializeObject<P2pNetMessage>(channelMsg.Message);
                ImplementationAddReceiptTimestamp(msg);
                lock(queueLock)
                    messageQueue.Add(msg); // queue it up
            });
        }

        protected override void ImplementationStopListening(string channel)
        {
            ConnectionMux.GetSubscriber().Unsubscribe(channel);
        }

        protected override string ImplementationNewP2pId()
        {
            return System.Guid.NewGuid().ToString();
        }

        protected override void ImplementationAddReceiptTimestamp(P2pNetMessage msg)
        {
            msg.rcptTime = P2pNetDateTime.NowMs;
        }

    }
}
