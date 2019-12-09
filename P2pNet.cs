using System;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

namespace P2pNet
{
    public interface IP2pNetClient
    {
        /// <returns>Hello data from remote peer. Probably JSON-encoded by the p2pnet client.</returns> 
        string P2pHelloData();
        void OnPeerJoined(string p2pId, string helloData);
        void OnPeerLeft(string p2pId);
        void OnP2pMsg(string from, string to, string payload);
    }
    public interface IP2pNet
    {
        void Loop(); /// <summary> needs to be called periodically (drives message pump + group handling)</summary>
        string GetId();
        /// <returns>Local peer's P2pNet ID.</returns>       
        string Join(string mainChannel);
        List<string> GetPeerIds();
        /// <returns>Remote peer;s HELLO data</returns> 
        string GetPeerData(string peerId);
        void Leave();
        void Send(string chan, string payload);
        void AddPeer(string peerId);
    }

    public class P2pNetPeer
    {       
        public string p2pId;
        public string helloData; 
        protected long firstHelloSentTs = 0; // when did we FIRST send a hello/hello req? (for knowing when to give up)
        // TODO: need to set the above either in the constructor (if it includes hello data)
        // or when we send a hello to a peer that has firstHelloSentTs == 0;
        protected long lastHeardTs = 0; // time when last heard from. 0 for never heard from
        protected long lastSentToTs = 0; // stamp for last message we sent (use to throttle pings somewhat)  
        protected long lastMsgId = 0; // Last msg rcvd from this peer. Each peer tags each mesage with a serial # (nextMsgId in P2PNetBase)

        protected int pingTimeoutMs;
        protected int dropTimeoutMs;        

        public P2pNetPeer(string _p2pId, int _pingMs, int _dropMs)
        {
            p2pId = _p2pId;
            pingTimeoutMs = _pingMs;
            dropTimeoutMs = _dropMs;
        }

        public bool HaveTriedToContact() => firstHelloSentTs > 0;
        public bool HaveHeardFrom() => helloData != null;

        public bool WeShouldSendHello() 
        {
            // Should we send hello to a node we've never heard from?
            // Yes if:
            // - If we've never sent a hello. 
            // - it's been more that a ping-time since we did.   
            if (HaveHeardFrom())
                return false; 
            if (!HaveTriedToContact())
                return true;
            return (P2pNetBase.nowMs - lastSentToTs) > pingTimeoutMs;                     
        }

        public bool HelloTimedOut()
        {
            long elapsed = P2pNetBase.nowMs - lastHeardTs;
            //if ( elapsed > _pingTimeoutSecs * 3 / 2)
            //    P2pNetTrace.Info($"Peer is late: {p.p2pID}");
            // TODO: worth pinging?
            return (elapsed > dropTimeoutMs);
        }

        public bool HasTimedOut() 
        {
            // Not hearing from them?
            long elapsed = P2pNetBase.nowMs - lastHeardTs;
            return (elapsed > dropTimeoutMs);            
        }

        public void UpdateLastHeardFrom() =>  lastHeardTs = P2pNetBase.nowMs;
        public void UpdateLastSentTo() =>  lastSentToTs = P2pNetBase.nowMs;

        public bool NeedsPing() => (P2pNetBase.nowMs - lastSentToTs) > pingTimeoutMs;

        public bool ValidateMsgId(long msgId)
        {
            // reject any new msg w/ id <= what we have already seen
            if (msgId <= lastMsgId)
                return false;
            lastMsgId = msgId;
            return true;
        }
    }

    public class P2pNetMessage
    {
        // Note that a P2pNetClient never sees this
        // TODO: How to make this "internal" and still allow P2pNetBase._Send() to be protected

        public const string MsgHello = "HELLO"; // recipient should reply
        public const string MsgHelloReply = "HRPLY"; // do not reply
        public const string MsgGoodbye = "BYE";
        public const string MsgPing = "PING";
        public const string MsgPingReply = "PNGRPLY";        
        public const string MsgAppl = "APPMSG";
        public string dstChannel;
        public string srcId;
        public long msgId;
        public string msgType;
        public string payload; // string or json-encoded application object

        public P2pNetMessage(string _dstChan, string _srcId, long _msgId, string _msgType, string _payload)
        {
            dstChannel = _dstChan;
            srcId = _srcId;
            msgId = _msgId;
            msgType = _msgType;
            payload = _payload;
        }
    }

    public abstract class P2pNetBase : IP2pNet
    {
        public static Dictionary<string, string> defaultConfig = new Dictionary<string, string>() 
        {
            {"pingMs", "7000"},
            {"dropMs", "15000"}
        };

        public Dictionary<string, string> config;
        protected string localId;
        protected string mainChannel; // broadcasts go here
        protected IP2pNetClient client;
        protected string connectionStr; // Transport-dependent format
        protected Dictionary<string, P2pNetPeer> peers;
        protected Dictionary<string, long> lastMsgIdSent; // last Id sent to each channel/peer
        public static long nowMs => DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        public P2pNetBase(IP2pNetClient _client, string _connectionStr, Dictionary<string, string> _config = null)
        {
            config = _config ?? defaultConfig;
            client = _client;
            connectionStr = _connectionStr;
            _InitJoinParams();
        }

        // IP2pNet
        public string GetId() => localId;
        public string Join(string _mainChannel)
        {
            _InitJoinParams();
            mainChannel = _mainChannel;
            localId = _Join(mainChannel);
            P2pNetTrace.Info(string.Format("*{0}: Join - Sending hello to main channel", localId));
            _SendHello(mainChannel, true);
            return localId;
        }
        public List<string> GetPeerIds() => peers.Keys.ToList();
        public string GetPeerData(string peerId)
        {
            try {
                return peers[peerId].helloData;
            } catch(KeyNotFoundException) {
                return null;
            }
        }

        public void Loop()
        {
            if (localId == null) 
                return; // Not connected so don't bother

            _Poll();

            List<P2pNetPeer> peersToDelete = new List<P2pNetPeer>();

            foreach( P2pNetPeer peer in peers.Values )
            {
                if (!peer.HaveHeardFrom())
                {
                    // Peer must've been added manually w/AddPeer()                     
                    if (peer.WeShouldSendHello())
                    {
                        _SendHello(peer.p2pId, true);
                    } else if (peer.HelloTimedOut()) {
                        // We've already sent one, and never heard back. Stop trying.
                        P2pNetTrace.Warn(string.Format("*{0}: Loop - Failed HelloTimedOut(): {1}", localId, peer.p2pId));                        
                        peersToDelete.Add(peer);
                    }
                } else {
                    // It's someone we know about
                    if (peer.HasTimedOut())
                    {
                        P2pNetTrace.Warn(string.Format("*{0}: Loop - Failed HasTimedOut(): {1}", localId, peer.p2pId));                        
                        peersToDelete.Add(peer);
                        client.OnPeerLeft(peer.p2pId);
                    }   
                }
            }

            foreach( P2pNetPeer p in peersToDelete)
                peers.Remove(p.p2pId);

            List<string> whoNeedsPing = peers.Values.Where( p => p.NeedsPing()).Select(p => p.p2pId).ToList();
            if ( whoNeedsPing.Count == 1)
                _SendPing(whoNeedsPing[0]);
            else if (whoNeedsPing.Count > 1)
                _SendPing( mainChannel);                

        }

        public void Send(string chan, string payload)
        {
            if (chan == localId)
            {
                client.OnP2pMsg(localId, localId, payload); // direct loopback
            } else {
                if (chan == mainChannel)
                    client.OnP2pMsg(localId, chan, payload); // main channnel loopback

                P2pNetTrace.Info(string.Format("*{0}: Send - sending appMsg to {1}", localId, (chan == mainChannel) ? "main channel" : chan));                  
                _DoSend(chan, P2pNetMessage.MsgAppl, payload);
            }
        }

        public void AddPeer(string peerId) {} // really only makes sense for direct-connection transports
        public void Leave()
        {
            _SendBye();
            _Leave();
            _InitJoinParams();
        }

        // Implementation methods
        protected abstract void _Poll();        
        protected abstract string _Join(string mainChannel);
        protected abstract bool _Send(P2pNetMessage msg);
        protected abstract void _Listen(string channel);
        protected abstract void _StopListening(string channel);
        protected abstract void _Leave();

        // Transport-independent tasks
        public void OnPingTimeout(P2pNetPeer p)
        {
            client.OnPeerLeft(p.p2pId); // TODO: should say that it wasn;t a good leave
            peers.Remove(p.p2pId);
        }

        protected void _DoSend(string dstChan, string msgType, string payload)
        {
            // Send() is the API for client messages
            long msgId = _NextMsgId(dstChan);
            P2pNetMessage p2pMsg = new P2pNetMessage(dstChan, localId, msgId, msgType, payload);
            if (_Send(p2pMsg))
                _UpdateSendStats(dstChan, msgId);
        }
        protected void _OnReceivedNetMessage(string srcChannel, P2pNetMessage msg)
        {
            if (msg.srcId == localId)
            {
                return; // main channel messages from local peer will show up here
            } else if (peers.ContainsKey(msg.srcId)) {             
                peers[msg.srcId].UpdateLastHeardFrom();
            }

            // TODO: get rid of switch
            switch(msg.msgType)
            {
                case P2pNetMessage.MsgAppl:
                    _OnAppMsg(srcChannel, msg);
                    break;
                case P2pNetMessage.MsgHello:
                case P2pNetMessage.MsgHelloReply:                
                    _OnHelloMsg(srcChannel, msg);
                    break;
                case P2pNetMessage.MsgGoodbye:
                    _OnByeMsg(srcChannel, msg);
                    break;
                case P2pNetMessage.MsgPing:
                    _OnPingMsg(srcChannel, msg);
                    break;                    
            }
        }
        protected void _OnAppMsg(string srcChannel, P2pNetMessage msg)
        {
            // dispatch a received client message
            if (!peers.ContainsKey(msg.srcId))
            {
                // Don't know this peer. This should not happen
                P2pNetTrace.Warn(string.Format("*{0}: _OnAppMsg - Unknown peer {1}", localId, msg.srcId));                
                return;
            }
            P2pNetTrace.Info(string.Format("*{0}: _OnAppMsg - msg from {1}", localId, msg.srcId));            
            client.OnP2pMsg(msg.srcId, msg.dstChannel, msg.payload);
        }
        protected void _InitJoinParams()
        {
            peers = new Dictionary<string, P2pNetPeer>();
            lastMsgIdSent = new Dictionary<string, long>();
            localId = null;
        }

        protected long _NextMsgId(string chan)
        {
            try {
                return lastMsgIdSent[chan] + 1;
            } catch (KeyNotFoundException){
                return 1;
            }
        }

        protected void _UpdateSendStats(string channel, long latestMsgId)
        {
            lastMsgIdSent[channel] = latestMsgId;

            if (channel == mainChannel)
                foreach (P2pNetPeer p in peers.Values)
                    p.UpdateLastSentTo();
            else
                peers[channel].UpdateLastSentTo();
        }

        // Some specific messages

        protected void _SendHello(string channel, bool requestReply)
        {
            string msgType = requestReply ?  P2pNetMessage.MsgHello : P2pNetMessage.MsgHelloReply;
            _DoSend(channel, msgType, client.P2pHelloData());
        }

        protected void _OnHelloMsg(string srcChannel, P2pNetMessage msg)
        {
            if (!peers.ContainsKey(msg.srcId))
            {
                P2pNetTrace.Info(string.Format("*{0}: _OnHelloMsg - Hello from {1}", localId, msg.srcId));                
                P2pNetPeer p = new P2pNetPeer(msg.srcId, int.Parse(config["pingMs"]), int.Parse(config["dropMs"]));
                p.helloData = msg.payload;
                p.UpdateLastHeardFrom();                
                peers[p.p2pId] = p;
                if ( msg.msgType == P2pNetMessage.MsgHello)
                {
                    P2pNetTrace.Info(string.Format("*{0}: _OnHelloMsg - replying to {1}", localId, p.p2pId));                
                    _SendHello(p.p2pId, false); // we don;t want a reply
                }
                P2pNetTrace.Info(string.Format("*{0}: _OnHelloMsg - calling client.({1})", localId, p.p2pId));               
                client.OnPeerJoined(p.p2pId, msg.payload);
            }
        }

        protected void _SendPing(string chan)
        {
            string toWhom = chan == mainChannel ? "Everyone" : chan;
            P2pNetTrace.Info(string.Format($"{localId}: _SendPing() - Sending to {toWhom}" ));            
            _DoSend(chan, P2pNetMessage.MsgPing, null);
        }

        protected void _OnPingMsg(string srcChannel, P2pNetMessage msg)
        {
            P2pNetTrace.Info(string.Format("*{0}: _OnPingMsg - Ping from {1}", localId, msg.srcId));            
            // Don't really do anything. We already called updateLastHeardFrom for the peer
        }

        protected void _SendBye()
        {
            object helloData = client.P2pHelloData();
            _DoSend(mainChannel, P2pNetMessage.MsgGoodbye, null);
        }

        protected void _OnByeMsg(string srcChannel, P2pNetMessage msg)
        {
           client.OnPeerLeft(msg.srcId);
            _StopListening(msg.srcId);
            peers.Remove(msg.srcId);
        }

    }
}
