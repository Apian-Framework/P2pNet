using System;
using System.Collections.Generic;

namespace P2pNet
{

    // This might be a stupid way to do loopback,
    // especially since messages to mainChannel and localId are already handled
    // in the base class
    public class P2pLoopback : P2pNetBase
    {
        List<string> listeningTo;
        List<P2pNetMessage> messageQueue;

        public P2pLoopback(IP2pNetClient _client, string _connectionString,  Dictionary<string, string> _config = null) : base(_client, _connectionString,  _config)
        {
            messageQueue = new List<P2pNetMessage>();
            listeningTo = new List<string>();
        }

        protected override void _Poll()
        {
            if (messageQueue.Count > 0)
            {
                // No locking is needed for local loopback
                List<P2pNetMessage> prevMessageQueue;
                prevMessageQueue = messageQueue;
                messageQueue = new List<P2pNetMessage>();

                foreach( P2pNetMessage msg in prevMessageQueue)
                {
                    _OnReceivedNetMessage(msg.dstChannel, msg);
                }
            }
        }

        protected override void _Join(string mainChannel)
        {
            listeningTo.Add(mainChannel);
            listeningTo.Add(localId);
        }

        protected override void _Leave()
        {
            messageQueue = new List<P2pNetMessage>();
            listeningTo = new List<string>();
        }
        protected override bool _Send(P2pNetMessage msg)
        {
            if (listeningTo.Contains(msg.dstChannel))
            {
                _AddReceiptTimestamp(msg);
                messageQueue.Add(msg);
            }
            return true;
        }

        protected override void _Listen(string channel)
        {
            listeningTo.Add(channel);
        }

        protected override void _StopListening(string channel)
        {
            listeningTo.Remove(channel);
        }

        protected override string _NewP2pId()
        {
            return System.Guid.NewGuid().ToString();
        }

        protected override void _AddReceiptTimestamp(P2pNetMessage msg) => msg.rcptTime = P2pNetDateTime.NowMs;

    }
}
