using System;
using System.Threading;
using System.Collections.Generic;

namespace P2pNet
{

    // This might be a stupid way to do loopback,
    // especially since messages to mainChannel and localId are already handled
    // in the base class
    public class P2pLoopback : IP2pNetCarrier

    {
        class JoinState
        {
            public SynchronizationContext mainSyncCtx;
            public List<string> listeningTo;
            public IP2pNetBase p2pBase;
        }

        private JoinState joinState;

        public P2pLoopback(string _connectionString)
        {
            ResetJoinVars();
        }

        private void ResetJoinVars()
        {
            joinState = null;
        }

        public void Poll()
        {
        }

        public  void Join(P2pNetChannelInfo mainChannel, IP2pNetBase _p2pBase, string localHelloData)
        {
            ResetJoinVars();
            joinState = new JoinState()
            {
                listeningTo = new List<string>(),
                p2pBase=_p2pBase,
                mainSyncCtx = SynchronizationContext.Current
            };

            Listen(_p2pBase.GetId());
            _p2pBase.OnNetworkJoined(mainChannel, localHelloData);
        }

        public void Leave()
        {
            ResetJoinVars();
        }

        public void Send(P2pNetMessage msg)
        {
            if (joinState.listeningTo.Contains(msg.dstChannel))
            {
                AddReceiptTimestamp(msg);
                if (joinState.mainSyncCtx != null)
                {
                    joinState.mainSyncCtx.Post( new SendOrPostCallback( (o) => {
                        joinState?.p2pBase?.OnReceivedNetMessage(msg.dstChannel, msg);
                    } ), null);
                } else {
                    joinState.p2pBase.OnReceivedNetMessage(msg.dstChannel, msg);
                }

            }
        }

        public void Listen(string channel)
        {
            joinState.listeningTo.Add(channel);
        }

        public void StopListening(string channel)
        {
            joinState.listeningTo.Remove(channel);
        }

        protected void AddReceiptTimestamp(P2pNetMessage msg) => msg.rcptTime = P2pNetDateTime.NowMs;

    }
}
