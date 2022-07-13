using System;
using System.Threading;
using System.Collections.Generic;
using Newtonsoft.Json;
using UniLog;
using UnityEngine;
using UnityLibp2p;

namespace P2pNet
{
#if  UNITY_WEBGL && !UNITY_EDITOR
    public class P2pJsLibp2p : IP2pNetCarrier, ILibp2pClient
    {
        class JoinState
        {
            public SynchronizationContext mainSyncCtx; // might be null
            public IP2pNetBase p2pBase;
            public P2pNetChannelInfo mainChannel;
            public string localHelloData;
        };

        private JoinState joinState;

        protected ILibp2p lib;

        private readonly Dictionary<string,string> connectOpts;


        public Libp2pPeerId localLibp2pId;

        public string ListenAddress { get; private set; }

        public bool IsConnected { get; private set;}


        public UniLogger logger;

        public P2pJsLibp2p( string _connectionString)
        {
            logger = UniLogger.GetLogger("P2pNet");

            // {  "relaybase":"<relatBaseMaddr>"
            //    "relayid":<relay connect peerId>
            //    "dialid":<pubsub peer id>  <== ignore this
            // }
            connectOpts = JsonConvert.DeserializeObject<Dictionary<string,string>>(_connectionString);

            ResetJoinVars();
        }

        private void ResetJoinVars()
        {
            joinState = null;
        }

        public void Join(P2pNetChannelInfo mainChannel, IP2pNetBase p2pBase, string localHelloData)
        {
            ResetJoinVars();

            Libp2pConfig configObj = Libp2pConfig.DefaultWebsocketConfig ; // by default

            configObj.config.peerDiscovery.bootstrap.enabled = true;
            configObj.config.peerDiscovery.bootstrap.list[0] = connectOpts["relaybase"]+connectOpts["relayid"];

            joinState = new JoinState()
            {
                p2pBase=p2pBase,
                mainChannel=mainChannel,
                localHelloData=localHelloData,
                mainSyncCtx = SynchronizationContext.Current
            };

            lib = Libp2p.Factory(this, configObj);

            // doesn't start until OnCreated...
        }


        public void Leave()
        {
            // FIXME: Do this

            // ConnectionMux.Close();
            // ConnectionMux =null;
            // customConnectFactory = null;
            // connectionString = null;
            // messageQueue = null;
        }

        public void Send(P2pNetMessage msg)
        {
            string msgJSON = JsonConvert.SerializeObject(msg);
            lib.Publish(msg.dstChannel, msgJSON);
        }

        public void Listen(string channel)
        {
            lib.Subscribe(channel);
        }

        public void StopListening(string channel)
        {
            lib.Unsubscribe(channel);
        }

        public void AddReceiptTimestamp(P2pNetMessage msg)
        {
            msg.rcptTime = P2pNetDateTime.NowMs;
        }

        public void Poll() {}

        // ILibp2pClient implementation
        public void OnCreated(Libp2pPeerId localPeer)
        {
            if (!lib.IsStarted)
            {
                localLibp2pId = localPeer;
                lib.Start();

                // results (async) in OnStarted
                // Also - because the relay in in the bootstrap list, there will be
                // a an OnConnected, too, with the relayer ID)
            }
        }
        public void OnStarted()
        {

            // wait until actually connectd (listen address AND started)
        }

        public void OnStopped()
        {
            //Log( $"\nLibp2p instance {lib.InstanceId} stopped.");
        }

        protected void _reportConnectedToNet()
        {
            IsConnected = true;
            Listen(joinState.p2pBase.GetId());

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

        public void OnListenAddress(List<string> addresses)
        {
            // We get this when we have connected to the realy and have been assigned a proxy
            //  "listen" address. At this point if we created the group and theso there are no other
            // members ( so connectOpts["dialid"] is the empty string), then we can consider
            // ourtselves "connected" to the group's network
            if (addresses.Count > 0)
            {
                if (!IsConnected)
                {
                    ListenAddress = addresses[0];

                    _reportConnectedToNet();

                }
            }
        }
        public void OnPeerDiscovery(Libp2pPeerId peerId)
        {
            //Log( $"\nRemote peer discovered: {peerId.id}");
        }
        public void OnConnectionEvent(Libp2pPeerId peerId, bool connected)
        {
            //Log( $"\n{(connected ? "Connected to" : "Disconnected from")}  remote peer: {peerId.id}");
            // if ( connected == true)
            // {
            //     if (IsConnected == false)
            //     {
            //         if (peerId.id == connectOpts["dialid"])
            //         {
            //             // we are now connected to a pubsub peer so can start talking
            //             _reportConnectedToNet();
            //         }
            //     }
            // }

        }
        public void OnMessage(string sourceId, string topic, string payload)
        {
            logger.Verbose($"P2pJsLibp2p OnMessage() thread: {Environment.CurrentManagedThreadId}");
            P2pNetMessage msg = JsonConvert.DeserializeObject<P2pNetMessage>(payload);

            AddReceiptTimestamp(msg);

            if (joinState.mainSyncCtx != null)
            {
                joinState.mainSyncCtx.Post( new SendOrPostCallback( (o) => {
                    logger.Verbose($"XXXXXXXX thread: {Environment.CurrentManagedThreadId}");
                    joinState.p2pBase.OnReceivedNetMessage(msg.dstChannel, msg);
                } ), null);
            } else {
                joinState.p2pBase.OnReceivedNetMessage(msg.dstChannel, msg);
            }
        }
        public void OnPing(string peerAddr, int latencyMs)
        {
            //Log( $"Ping returned from {peerAddr} - latency: {latencyMs} ms");
        }

    }
#else

    // Unity WEBGL only

#endif
}
