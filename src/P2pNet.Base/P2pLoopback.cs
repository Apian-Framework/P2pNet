using System;
using System.Collections.Generic;

namespace P2pNet
{

    // This might be a stupid way to do loopback,
    // especially since messages to mainChannel and localId are already handled
    // in the base class
    public class P2pLoopback : IP2pNetCarrier

    {
        List<string> listeningTo;
        List<P2pNetMessage> messageQueue;
        IP2pNetBase p2pBase;

        public P2pLoopback(string _connectionString)
        {
            ResetJoinVars();
        }

        private void ResetJoinVars()
        {
            messageQueue = new List<P2pNetMessage>();
            listeningTo = new List<string>();
        }

        public void Poll()
        {
            if (messageQueue.Count > 0)
            {
                // No locking is needed for local loopback
                List<P2pNetMessage> prevMessageQueue;
                prevMessageQueue = messageQueue;
                messageQueue = new List<P2pNetMessage>();

                foreach( P2pNetMessage msg in prevMessageQueue)
                {
                    p2pBase.OnReceivedNetMessage(msg.dstChannel, msg);
                }
            }
        }

        public  void Join(P2pNetChannelInfo mainChannel, IP2pNetBase _p2pBase, string localHelloData)
        {
            ResetJoinVars();
            p2pBase = _p2pBase;
            Listen(p2pBase.GetId());
            p2pBase.OnNetworkJoined(mainChannel, localHelloData);
        }

        public void Leave()
        {
            ResetJoinVars();
        }
        public void Send(P2pNetMessage msg)
        {
            if (listeningTo.Contains(msg.dstChannel))
            {
                AddReceiptTimestamp(msg);
                messageQueue.Add(msg);
            }
        }

        public void Listen(string channel)
        {
            listeningTo.Add(channel);
        }

        public void StopListening(string channel)
        {
            listeningTo.Remove(channel);
        }

        protected void AddReceiptTimestamp(P2pNetMessage msg) => msg.rcptTime = P2pNetDateTime.NowMs;

    }
}
