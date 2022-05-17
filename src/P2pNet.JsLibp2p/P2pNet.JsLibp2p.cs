using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Threading.Tasks;
using UniLog;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
using AOT;
#endif

namespace P2pNet
{

    public class P2pNetJsLibp2p : P2pNetBase
    {
        public static Dictionary<string, P2pNetJsLibp2p> LibInstances = new Dictionary<string, P2pNetJsLibp2p>();

        public bool IsConnected {get; private set; }
        private readonly object queueLock = new object();
        private List<P2pNetMessage> messageQueue;
        protected string connectionString;
        protected string libInstanceId; // interop code with browser is static
        protected P2pNetChannelInfo mainChannel;
        protected string localPeerId; // TODO: arenl;t these in the parent class?
        protected string localHelloData;


#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void P2pNetJsLibp2p_JoinNetwork(string libId,
            string connectionStr,
            Action< string, string> connectionCb,
            Action< string, string, string> msgCb
        );

        [DllImport("__Internal")]
        private static extern void P2pNetJsLibp2p_LeaveNetwork(string libId);

        [DllImport("__Internal")]
        private static extern void P2pNetJsLibp2p_Subscribe(string libId, string channel);

        [DllImport("__Internal")]
        private static extern void P2pNetJsLibp2p_Unsubscribe(string libId, string channel);

        [DllImport("__Internal")]
        private static extern void P2pNetJsLibp2p_Publish(string libId, string channel, string msg);

        // Static callbacks from javascript
        [MonoPInvokeCallback(typeof(Action<string,string>))]
        public static void Libp2pConnectionCb( string libId, string peerId) {
            //UniLogger.GetLogger("P2pNet").Warn($"#### Hey! I got called!!! LibId: {libId} PeerId: {peerId}");
            try {
                LibInstances[libId].OnConnection(peerId);
            } catch (Exception ex) {
                UniLogger.GetLogger("P2pNet").Error(ex.Message);
            }
        }

        [MonoPInvokeCallback(typeof(Action<string, string, string>))]
        public static void Libp2pMessageCb( string libId, string channel, string msgJson) {
            UniLogger.GetLogger("P2pNet").Warn($"#### Hey! Libp2pMessageCb got called!!! Channel: {channel} MsgJSon: {msgJson}");
            try {
                LibInstances[libId].OnMessage(channel, msgJson);
            } catch (Exception ex) {
                UniLogger.GetLogger("P2pNet").Error(ex.Message);
            }

        }

#else

//#warning P2NetJsLibp2p only works in WEBGL builds
        private static void P2pNetJsLibp2p_JoinNetwork( string libId,
            string connectionStr,
            Action< string, string> connectionCb,
            Action< string, string, string> msgCb
        ) {}
        private static void P2pNetJsLibp2p_Unsubscribe(string libId, string channel) {}
        private static void P2pNetJsLibp2p_Subscribe(string libId, string channel) {}
        private static void P2pNetJsLibp2p_Publish(string libId, string channel, string msg) {}
        private static void P2pNetJsLibp2p_LeaveNetwork(string libId) {}

        public static void Libp2pConnectionCb(string libId, string channel) {}
        public static void Libp2pMessageCb(string libId, string channel, string msgJson) {}

#endif

        public P2pNetJsLibp2p(IP2pNetClient _client, string _connectionString) : base(_client, _connectionString)
        {
            // valid connection string is typically: "<host>,password=<password>"

            libInstanceId = Guid.NewGuid().ToString();
            LibInstances[libInstanceId] = this;
            connectionString = _connectionString;
            messageQueue = new List<P2pNetMessage>();
            GetId();
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

        protected override void ImplementationJoin(P2pNetChannelInfo _mainChannel, string _localPeerId, string _localHelloData)
        {
            mainChannel = _mainChannel;
            localPeerId = _localPeerId;
            localHelloData = _localHelloData;
            P2pNetJsLibp2p_JoinNetwork( libInstanceId, connectionString, Libp2pConnectionCb, Libp2pMessageCb );

            // try {
            //     ConnectionMux =  customConnectFactory != null ? customConnectFactory(connectionString) : ConnectionMultiplexer.Connect(connectionString); // Use the passed-in test mux instance if supplied
            // } catch (StackExchange.Redis.RedisConnectionException ex) {
            //     logger.Debug(string.Format("P2pRedis Ctor: StackExchange.Redis.RedisConnectionException:{0}", ex.Message));
            //     throw( new Exception($"{_GuessRedisProblem(ex.Message)}"));
            // } catch (System.ArgumentException ex) {
            //     logger.Debug(string.Format("P2pRedis Ctor: System.ArgumentException:{0}", ex.Message));
            //     throw( new Exception($"Bad connection string: {ex.Message}"));
            // }

        }

        public void OnConnection(string libp2pPeerId)
        {
            //UniLogger.GetLogger("P2pNet").Warn($"OnConnection() called for libp2pPeer: {libp2pPeerId}");
            if (!IsConnected)
            {
                IsConnected = true;
                UniLogger.GetLogger("P2pNet").Warn($"OnConnection() called for libp2pPeer {libp2pPeerId}");
                ImplementationListen(localPeerId);
                OnNetworkJoined(mainChannel, localHelloData);
            }
        }


        protected override void ImplementationLeave()
        {
            // TODO: Implement
            connectionString = null;
            messageQueue = null;
            P2pNetJsLibp2p_LeaveNetwork(libInstanceId);
        }

        protected override void ImplementationSend(P2pNetMessage msg)
        {
            string msgJSON = JsonConvert.SerializeObject(msg);
            P2pNetJsLibp2p_Publish(libInstanceId, msg.dstChannel, msgJSON);
        }

        protected override void ImplementationListen(string channel)
        {
            P2pNetJsLibp2p_Subscribe(libInstanceId, channel);
        }

        public void OnMessage(string channel, string msgJson)
        {
            UniLogger.GetLogger("P2pNet").Warn($"_OnMessage() called for channel {channel}");
        }



        // private void _ListenSequential(string channel)
        // {
        //     // var rcvChannel = ConnectionMux.GetSubscriber().Subscribe(channel);

        //     // rcvChannel.OnMessage(channelMsg =>
        //     // {
        //     //     P2pNetMessage msg = JsonConvert.DeserializeObject<P2pNetMessage>(channelMsg.Message);
        //     //     ImplementationAddReceiptTimestamp(msg);
        //     //     lock(queueLock)
        //     //         messageQueue.Add(msg); // queue it up
        //     // });
        // }

        protected override void ImplementationStopListening(string channel)
        {
            P2pNetJsLibp2p_Unsubscribe(libInstanceId, channel);
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
