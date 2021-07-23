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

        public P2pLoopback(IP2pNetClient _client, string _connectionString) : base(_client, _connectionString)
        {
            messageQueue = new List<P2pNetMessage>();
            listeningTo = new List<string>();
        }

        protected override void ImplementationPoll()
        {
            if (messageQueue.Count > 0)
            {
                // No locking is needed for local loopback
                List<P2pNetMessage> prevMessageQueue;
                prevMessageQueue = messageQueue;
                messageQueue = new List<P2pNetMessage>();

                foreach( P2pNetMessage msg in prevMessageQueue)
                {
                    OnReceivedNetMessage(msg.dstChannel, msg);
                }
            }
        }

        protected override void ImplementationJoin(P2pNetChannelInfo mainChannel, string localPeerId, string localHelloData)
        {
            ImplementationListen(localPeerId);
            OnNetworkJoined(mainChannel, localHelloData);

        }

        protected override void ImplementationLeave()
        {
            messageQueue = new List<P2pNetMessage>();
            listeningTo = new List<string>();
        }
        protected override void ImplementationSend(P2pNetMessage msg)
        {
            if (listeningTo.Contains(msg.dstChannel))
            {
                ImplementationAddReceiptTimestamp(msg);
                messageQueue.Add(msg);
            }
        }

        protected override void ImplementationListen(string channel)
        {
            listeningTo.Add(channel);
        }

        protected override void ImplementationStopListening(string channel)
        {
            listeningTo.Remove(channel);
        }

        protected override string ImplementationNewP2pId()
        {
            return System.Guid.NewGuid().ToString();
        }

        protected override void ImplementationAddReceiptTimestamp(P2pNetMessage msg) => msg.rcptTime = P2pNetDateTime.NowMs;

    }
}
