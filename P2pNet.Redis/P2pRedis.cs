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
        public ConnectionMultiplexer RedisCon {get; private set; } = null;

        public P2pRedis(IP2pNetClient _client, string _connectionString,  Dictionary<string, string> _config = null) : base(_client, _connectionString,  _config)
        {
            RedisCon = ConnectionMultiplexer.Connect(_connectionString);
            messageQueue = new List<P2pNetMessage>();
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

        protected override void _Join(string mainChannel)
        {
            _Listen(mainChannel);
            _Listen(localId);
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
            RedisCon.GetSubscriber().Subscribe(channel, (rcvChannel, msgJSON) => {
                P2pNetMessage msg = JsonConvert.DeserializeObject<P2pNetMessage>(msgJSON);
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
            msg.rcptTime = NowMs;
        }

    }
}
