using System;
using System.Threading;
using System.Collections.Generic;
using StackExchange.Redis;
using Newtonsoft.Json;
using UniLog;

namespace P2pNet
{

    public class P2pRedis : IP2pNetCarrier
    {
        class JoinState
        {
            public SynchronizationContext mainSyncCtx;
            public IP2pNetBase p2pBase;
            public P2pNetChannelInfo mainChannel;
            public string localHelloData;
            public IConnectionMultiplexer connectionMux;
        };
        private JoinState joinState;

        protected Func<string, IConnectionMultiplexer> customConnectFactory;
        protected UniLogger logger;
        string connectionStr;

        public P2pRedis(string _connectionString, Func<string, IConnectionMultiplexer> _muxConnectFactory=null)
        {
            logger = UniLogger.GetLogger("P2pNet");
            ResetJoinVars();
            // valid connection string is typically: "<host>,password=<password>"
            customConnectFactory =  _muxConnectFactory;
            connectionStr = _connectionString;
        }

        private void ResetJoinVars()
        {
            joinState = null;
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


        public  void Poll() {}

        public void Join(P2pNetChannelInfo mainChannel, IP2pNetBase p2pBase, string localHelloData)
        {
            ResetJoinVars();

            joinState = new JoinState()
            {
                p2pBase=p2pBase,
                mainChannel=mainChannel,
                localHelloData=localHelloData,
                mainSyncCtx = SynchronizationContext.Current
            };

            try {
                joinState.connectionMux =  customConnectFactory != null ? customConnectFactory(connectionStr) : ConnectionMultiplexer.Connect(connectionStr); // Use the passed-in test mux instance if supplied
            } catch (StackExchange.Redis.RedisConnectionException ex) {
                logger.Debug(string.Format("P2pRedis Ctor: StackExchange.Redis.RedisConnectionException:{0}", ex.Message));
                throw( new Exception($"{_GuessRedisProblem(ex.Message)}"));
            } catch (System.ArgumentException ex) {
                logger.Debug(string.Format("P2pRedis Ctor: System.ArgumentException:{0}", ex.Message));
                throw( new Exception($"Bad connection string: {ex.Message}"));
            }
            Listen(p2pBase.GetId());

            p2pBase.OnNetworkJoined(mainChannel, localHelloData);
        }

        public void Leave()
        {
            joinState.connectionMux?.Close();
            ResetJoinVars();
        }

        public void Send(P2pNetMessage msg)
        {
            string msgJSON = JsonConvert.SerializeObject(msg);
            joinState.connectionMux.GetSubscriber().PublishAsync(msg.dstChannel, msgJSON);
        }

        public void Listen(string channel)
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
            var rcvChannel = joinState.connectionMux.GetSubscriber().Subscribe(channel);

            rcvChannel.OnMessage(channelMsg =>
            {
                P2pNetMessage msg = JsonConvert.DeserializeObject<P2pNetMessage>(channelMsg.Message);
                AddReceiptTimestamp(msg);

                if (joinState.mainSyncCtx != null)
                {
                    joinState.mainSyncCtx.Post( new SendOrPostCallback( (o) => {
                        joinState?.p2pBase?.OnReceivedNetMessage(msg.dstChannel, msg);
                    } ), null);
                } else {
                    joinState.p2pBase.OnReceivedNetMessage(msg.dstChannel, msg);
                }

            });
        }

        public void StopListening(string channel)
        {
            joinState.connectionMux.GetSubscriber().Unsubscribe(channel);
        }

        protected  void AddReceiptTimestamp(P2pNetMessage msg)
        {
            msg.rcptTime = P2pNetDateTime.NowMs;
        }

    }
}
