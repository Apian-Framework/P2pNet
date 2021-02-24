using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UniLog;

namespace P2pNet
{
    // ReSharper disable InconsistentNaming
    // Problem here is that "p2p" is a word: "peer-to-peer" and the default .NET ReSharper rules dealing with digits result
    // in dumb stuff, like a field called "_p2PFooBar" with the 2nd P capped.
    public interface IP2pNetClient
    {
        string P2pHelloData(); // Hello data FOR remote peer. Probably JSON-encoded by the p2pnet client.
        void OnPeerJoined(string p2pId, string helloData);
        void OnPeerSync(string p2pId, long clockOffsetMs, long netLagMs);
        void OnPeerLeft(string p2pId);
        void OnClientMsg(string from, string to, long msSinceSent, string payload);
    }

    public interface IP2pNet
    {
        // ReSharper disable UnusedMember.Global
        void Loop(); // needs to be called periodically (drives message pump + group handling)
        string GetId(); // Local peer's P2pNet ID.
        string GetMainChannel();
        void Join(string mainChannel);
        void AddSubchannel(string subChannel);
        void RemoveSubchannel(string subChannel);
        List<string> GetPeerIds();
        string GetPeerData(string peerId); // Remote peer's HELLO data
        PeerClockSyncData GetPeerClockSyncData(string peerId);
        void Leave();
        void Send(string chan, string payload);
        void AddPeer(string peerId);
        // ReSharper enable UnusedMember.Global
    }

     public class P2pNetMessage
    {
        // Note that a P2pNetClient never sees this
        // TODO: How to make this "internal" and still allow P2pNetBase._Send() to be protected
        public const string MsgHello = "HELLO"; // recipient should reply
        public const string MsgHelloReply = "HRPLY"; // do not reply
        public const string MsgGoodbye = "BYE";
        public const string MsgPing = "PING";
        public const string MsgAppl = "APPMSG";
        public const string MsgSync = "SYNC"; // clock sync


        public string dstChannel;
        public string srcId;
        public long msgId;
        public long sentTime; // millisecs timestamp at sender
        public long rcptTime; // millisecs timestamp at recipient (here)
        public string msgType;
        public string payload; // string or json-encoded application object

        public P2pNetMessage(string _dstChan, string _srcId, long _msgId, string _msgType, string _payload)
        {
            dstChannel = _dstChan;
            srcId = _srcId;
            msgId = _msgId;
            msgType = _msgType;
            payload = _payload;
            sentTime = -1; // gets set on send
            rcptTime = -1; // gets set on receipt
        }
    }

    public class SyncPayload
    {
        public long t0; //  t0 to originator
        public long t1; // t1 for org
        public long t2; //t2 for org, t0 for recip
        public long t3; // t3 for org, t1 for recip
        public long t4; // t2 for recip - t2 for it doesn't need to be in payload
        public SyncPayload() {t0=0; t1=0; t2=0; t3=0;}
        public override string ToString() => $"{{t0:{t0} t1:{t1} t2:{t2} t3:{t3}}}";
    }

    public abstract class P2pNetBase : IP2pNet
    {
        public static Dictionary<string, string> defaultConfig = new Dictionary<string, string>()
        {
            {"pingMs", "7000"},
            {"dropMs", "15000"},
            {"syncMs", "30000"}  // clock sync
        };

        public Dictionary<string, string> config;
        protected string localId;
        protected IP2pNetClient client;
        protected string connectionStr; // Transport-dependent format
        protected string mainChannel; // broadcasts go here
        protected Dictionary<string, P2pNetPeer> peers;
        protected List<string> subChannels; // other non-peer channels we are using
        protected Dictionary<string, long> lastMsgIdSent; // last Id sent to each channel/peer
        public UniLogger logger;

        //public static long NowMs => DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

        public P2pNetBase(IP2pNetClient _client, string _connectionStr, Dictionary<string, string> _config = null)
        {
            config = _config ?? defaultConfig;
            client = _client;
            connectionStr = _connectionStr;
            logger = UniLogger.GetLogger("P2pNet");
            _InitJoinParams();
            localId = _NewP2pId();
        }

        // IP2pNet

        public string GetId() => localId;

        public string GetMainChannel() => mainChannel;

        public void Join(string _mainChannel)
        {
            _InitJoinParams();
            mainChannel = _mainChannel;
            _Join(mainChannel);
            logger.Info(string.Format("*{0}: Join - Sending hello to main channel", localId));
            _SendHello(mainChannel, true);

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

        public PeerClockSyncData GetPeerClockSyncData(string peerId)
        {
            try {
                P2pNetPeer p = peers[peerId];
                return new PeerClockSyncData(p.p2pId, p.MsSinceClockSync, p.ClockOffsetMs, p.NetworkLagMs);
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
                        logger.Warn(string.Format("*{0}: Loop - Failed HelloTimedOut(): {1}", localId, peer.p2pId));
                        peersToDelete.Add(peer);
                    }
                } else {
                    // It's someone we know about
                    if (peer.HasTimedOut())
                    {
                        logger.Warn(string.Format("*{0}: Loop - Failed HasTimedOut(): {1}", localId, peer.p2pId));
                        peersToDelete.Add(peer);
                        client.OnPeerLeft(peer.p2pId);
                    }
                    else if (peer.ClockNeedsSync() )
                    {
                        _SendSync(peer.p2pId); // start
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
                client.OnClientMsg(localId, localId, 0, payload); // direct loopback
            } else {
                if (chan == mainChannel || subChannels.Contains(chan))
                    client.OnClientMsg(localId, chan, 0, payload); // broadcast channnel loopback

                logger.Debug(string.Format("*{0}: Send - sending appMsg to {1}", localId, (chan == mainChannel) ? "main channel" : chan));
                _DoSend(chan, P2pNetMessage.MsgAppl, payload);
            }
        }

        public void AddPeer(string peerId) {} // really only makes sense for direct-connection transports

        public void AddSubchannel(string chan)
        {
            if (!subChannels.Contains(chan))
            {
                logger.Info($"Listening to subchannel: {chan}");
                subChannels.Add(chan);
                _Listen(chan);
            }
        }

        public void RemoveSubchannel(string chan)
        {
            if (subChannels.Contains(chan))
            {
                subChannels.Remove(chan);
                _StopListening(chan);
            }
        }

        public void Leave()
        {
            _SendBye();
            _Leave();
            _InitJoinParams();
        }

        // Implementation methods
        protected abstract void _Poll();
        protected abstract void _Join(string mainChannel);
        protected abstract bool _Send(P2pNetMessage msg);
        protected abstract void _Listen(string channel);
        protected abstract void _StopListening(string channel);
        protected abstract void _Leave();
        protected abstract string _NewP2pId();
        protected abstract void _AddReceiptTimestamp(P2pNetMessage msg);

        // Transport-independent tasks

        // TODO: No longer used. Remove.  &&&&
        // public void OnPingTimeout(P2pNetPeer p)
        // {
        //     client.OnPeerLeft(p.p2pId); // TODO: should say that it wasn;t a good leave
        //     peers.Remove(p.p2pId);
        // }

        protected void _DoSend(string dstChan, string msgType, string payload)
        {
            // Send() is the API for client messages
            long msgId = _NextMsgId(dstChan);
            P2pNetMessage p2pMsg = new P2pNetMessage(dstChan, localId, msgId, msgType, payload);
            p2pMsg.sentTime = P2pNetDateTime.NowMs; // should not happen in ctor
            if (_Send(p2pMsg))
                _UpdateSendStats(dstChan, msgId);
        }

        protected void _OnReceivedNetMessage(string srcChannel, P2pNetMessage msg)
        {
            // Local messages have already been processed
            if (msg.srcId == localId)
                return; // main channel messages from local peer will show up here

            if (peers.ContainsKey(msg.srcId)) {
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
                case P2pNetMessage.MsgSync:
                    _OnSyncMsg(msg.srcId, msg);
                    break;
            }
        }

        protected void _OnAppMsg(string srcChannel, P2pNetMessage msg)
        {
            // dispatch a received client message
            if ( peers.TryGetValue( msg.srcId, out P2pNetPeer peer))
            {
                long remoteMsNow = P2pNetDateTime.NowMs + peer.ClockOffsetMs;
                long msSinceSend = remoteMsNow - msg.sentTime;

                if (msSinceSend < 0)
                {
                    logger.Debug($"_OnAppMsg() msg from {msg.srcId} w/lag < 0: {msSinceSend}");
                    msSinceSend = 0;
                }

                logger.Debug(string.Format("_OnAppMsg - msg from {0}",  msg.srcId));
                client.OnClientMsg(msg.srcId, msg.dstChannel, msSinceSend, msg.payload);

            } else {
                logger.Warn(string.Format("*{0}: _OnAppMsg - Unknown peer {1}", localId, msg.srcId));
            }
        }

        protected void _InitJoinParams()
        {
            peers = new Dictionary<string, P2pNetPeer>();
            lastMsgIdSent = new Dictionary<string, long>();
            mainChannel = null;
            subChannels = new List<string>();
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
                    p.UpdateLastSentTo(); // so we don't ping until it's needed
            else
                if (peers.Keys.Contains(channel)) // TODO: Channel messages other then mainChannel don't count for ping timeouts.
                    peers[channel].UpdateLastSentTo();
        }

        // Some specific messages

        protected void _SendHello(string channel, bool requestReply)
        {
            // TODO: Instead of sending HelloData() directly there should be a HelloMsgPayload class
            string msgType = requestReply ?  P2pNetMessage.MsgHello : P2pNetMessage.MsgHelloReply;
            _DoSend(channel, msgType, client.P2pHelloData());
        }

        protected void _OnHelloMsg(string srcChannel, P2pNetMessage msg)
        {
            if (!peers.ContainsKey(msg.srcId))
            {
                logger.Verbose(string.Format("*{0}: _OnHelloMsg - Hello from {1}", localId, msg.srcId));
                // TODO: should jsut send config dict
                P2pNetPeer p = new P2pNetPeer(msg.srcId, int.Parse(config["pingMs"]), int.Parse(config["dropMs"]), int.Parse(config["syncMs"]));
                p.helloData = msg.payload;
                p.UpdateLastHeardFrom();
                peers[p.p2pId] = p;
                if ( msg.msgType == P2pNetMessage.MsgHello)
                {
                    logger.Verbose(string.Format("*{0}: _OnHelloMsg - replying to {1}", localId, p.p2pId));
                    _SendHello(p.p2pId, false); // we don;t want a reply
                }
                logger.Verbose(string.Format("*{0}: _OnHelloMsg - calling client.({1})", localId, p.p2pId));
                client.OnPeerJoined(p.p2pId, msg.payload);
            }
        }

        protected void _SendPing(string chan)
        {
            string toWhom = chan == mainChannel ? "Everyone" : chan;
            logger.Verbose(string.Format($"{localId}: _SendPing() - Sending to {toWhom}" ));
            _DoSend(chan, P2pNetMessage.MsgPing, null);
        }

        protected void _OnPingMsg(string srcChannel, P2pNetMessage msg)
        {
            logger.Verbose(string.Format("*{0}: _OnPingMsg - Ping from {1}", localId, msg.srcId));
            // Don't really do anything. We already called updateLastHeardFrom for the peer
        }

        protected void _SendBye()
        {
            _DoSend(mainChannel, P2pNetMessage.MsgGoodbye, null);
        }

        protected void _OnByeMsg(string srcChannel, P2pNetMessage msg)
        {
           client.OnPeerLeft(msg.srcId);
            _StopListening(msg.srcId);
            peers.Remove(msg.srcId);
        }

        // NOTE: sync packets use the actual message timestamps. So, for insntance, when the
        // first Sync is sent t0 is NOT set - it gets set by the recipient from the
        // sentTime field. Liekwise, when it gets to _OnSynMsg, the receipt t1 or t3
        // is set from the incoming msg rcvdTime field.
        protected void _SendSync(string dest, SyncPayload _payload=null)
        {
            SyncPayload payload = _payload ?? new SyncPayload();
            peers[dest].ReportSyncProgress();
            // payload "sent time" gets set by receiver.
            _DoSend(dest, P2pNetMessage.MsgSync, JsonConvert.SerializeObject(payload));
        }

        protected void _OnSyncMsg(string from, P2pNetMessage msg)
        {
            if ( peers.TryGetValue(from, out P2pNetPeer peer))
            {
                SyncPayload payload = JsonConvert.DeserializeObject<SyncPayload>(msg.payload);
                if (payload.t0 == 0)
                {
                    // This was the first hop from the originator
                    payload.t0 = msg.sentTime;
                    payload.t1 = msg.rcptTime;
                    peer.ReportSyncProgress();
                    _DoSend(from, P2pNetMessage.MsgSync, JsonConvert.SerializeObject(payload)); // send reply
                } else if (payload.t2 == 0) {
                    // We are the originator getting our sync back
                    payload.t2 = msg.sentTime;
                    payload.t3 = msg.rcptTime;
                    _DoSend(from, P2pNetMessage.MsgSync, JsonConvert.SerializeObject(payload)); // send reply
                    peer.UpdateClockSync(payload.t0, payload.t1, payload.t2, payload.t3);
                    logger.Verbose($"Synced (org) {from} Lag: {peer.NetworkLagMs}, Offset: {peer.ClockOffsetMs}");
                    client.OnPeerSync(peer.p2pId, peer.ClockOffsetMs, peer.NetworkLagMs);
                } else {
                    // we're the recipient and it's done
                    peer.UpdateClockSync(payload.t2, payload.t3, msg.sentTime, msg.rcptTime);
                    logger.Verbose($"Synced (rcp) {from} Lag: {peer.NetworkLagMs}, Offset: {peer.ClockOffsetMs}");
                    client.OnPeerSync(peer.p2pId, peer.ClockOffsetMs, peer.NetworkLagMs);
                }
            } else {
               logger.Warn($"Got sync from unknown peer: {from}. Ignoring.");
            }
        }

    }
}
