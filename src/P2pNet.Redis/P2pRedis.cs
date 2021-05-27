using System;
using System.Collections.Generic;
using StackExchange.Redis;
using Newtonsoft.Json;

namespace P2pNet
{

    public class P2pRedis : P2pNetBase
    {
        private readonly object queueLock = new object();
        List<P2pNetMessage> messageQueue;
        public IConnectionMultiplexer connectionMux {get; private set; } = null;
        protected Func<string, IConnectionMultiplexer> customConnectFactory;
        protected string connectionString;

        public P2pRedis(IP2pNetClient _client, string _connectionString, Func<string, IConnectionMultiplexer> _muxConnectFactory=null) : base(_client, _connectionString)
        {
            // valid connection string is typically: "<host>,password=<password>"
            customConnectFactory =  _muxConnectFactory;
            connectionString = _connectionString;
            messageQueue = new List<P2pNetMessage>();
        }

        private string GuessRedisProblem(string exMsg)
        {
            // RedisConnectionException messages can be pretty long and unhelpful
            // to the user, but the Message property is the only indication what sort
            // of problem has occurred.
            string msg = exMsg;
            if (exMsg.Contains("authentication"))
                msg = "Redis suthentication failure";
            if (exMsg.Contains("UnableToConnect on"))
                msg = "Unable to connect to Redis host"; // TODO: parse the bad host out of the message and include it
            return msg;
        }

        protected override void _Poll()
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
                    _OnReceivedNetMessage(msg.dstChannel, msg);
                }
            }
        }

        protected override void _Join(P2pNetChannelInfo mainChannel, string localPeerId)
        {

            try {
                connectionMux =  customConnectFactory != null ? customConnectFactory(connectionString) : ConnectionMultiplexer.Connect(connectionString); // Use the passed-in test mux instance if supplied
            } catch (StackExchange.Redis.RedisConnectionException ex) {
                logger.Debug(string.Format("P2pRedis Ctor: StackExchange.Redis.RedisConnectionException:{0}", ex.Message));
                throw( new Exception($"{GuessRedisProblem(ex.Message)}"));
            } catch (System.ArgumentException ex) {
                logger.Debug(string.Format("P2pRedis Ctor: System.ArgumentException:{0}", ex.Message));
                throw( new Exception($"Bad connection string: {ex.Message}"));
            }
            _Listen(localPeerId);
        }

        protected override void _Leave()
        {
            connectionMux.Close();
            connectionMux =null;
            customConnectFactory = null;
            connectionString = null;
            messageQueue = null;
        }

        protected override bool _Send(P2pNetMessage msg)
        {
            string msgJSON = JsonConvert.SerializeObject(msg);
            connectionMux.GetSubscriber().PublishAsync(msg.dstChannel, msgJSON);
            return true;
        }

        protected override void _Listen(string channel)
        {
            //_ListenConcurrent(channel);
            _ListenSequential(channel);
        }

        protected  void _ListenConcurrent(string channel)
        {
            connectionMux.GetSubscriber().Subscribe(channel, (rcvChannel, msgJSON) => {
                P2pNetMessage msg = JsonConvert.DeserializeObject<P2pNetMessage>(msgJSON);
                _AddReceiptTimestamp(msg);
                lock(queueLock)
                    messageQueue.Add(msg); // queue it up
            });
        }

        protected void _ListenSequential(string channel)
        {
            var rcvChannel = connectionMux.GetSubscriber().Subscribe(channel);

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
            connectionMux.GetSubscriber().Unsubscribe(channel);
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
