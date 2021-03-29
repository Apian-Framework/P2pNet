using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UniLog;
using static UniLog.UniLogger; // for SID()

namespace P2pNet
{
    // ReSharper disable InconsistentNaming
    // Problem here is that "p2p" is a word: "peer-to-peer" and the default .NET ReSharper rules dealing with digits result
    // in dumb stuff, like a field called "_p2PFooBar" with the 2nd P capped.
    public interface IP2pNetClient
    {
        void OnPeerJoined(string channel, string p2pId, string helloData);
        void OnPeerMissing(string channel, string p2pId);
        void OnPeerReturned(string channel, string p2pId);
        void OnPeerLeft(string channel, string p2pId);
        void OnPeerSync(string channel, string p2pId, long clockOffsetMs, long netLagMs);
        void OnClientMsg(string from, string toChan, long msSinceSent, string payload);
    }

    public interface IP2pNet
    {
        // ReSharper disable UnusedMember.Global
        void Loop(); // needs to be called periodically (drives message pump + group handling)
        string GetId(); // Local peer's P2pNet ID.
        P2pNetChannel GetMainChannel();
        void Join(P2pNetChannelInfo mainChannel, string helloData);
        void AddSubchannel(P2pNetChannelInfo subChannel, string helloData);
        void RemoveSubchannel(string subChannelId);
        List<string> GetPeerIds();
        string GetPeerData(string channelId, string peerId); // Remote peer's HELLO data
        PeerClockSyncData GetPeerClockSyncData(string peerId);
        void Leave();
        void Send(string chan, string payload);
        void AddPeer(string peerId);
        void RemovePeer(string peerId);
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

    public class HelloPayload
    {
        public string channelId; // a Hello is often sent straight to a peer - over the peerId channel
        public string channelHelloData;
        public HelloPayload(string chId, string helloData) {channelId = chId; channelHelloData = helloData;}
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
        // FIXME: migrate this data as default to wherever it is supposed to go, then delete this comment clock
        // public static Dictionary<string, string> defaultConfig = new Dictionary<string, string>()
        // {
        //     {"pingMs", "7000"},
        //     {"dropMs", "15000"},
        //     {"syncMs", "30000"}  // clock sync
        // };
        //public Dictionary<string, string> config;

        protected string localId;
        protected IP2pNetClient client;
        protected string connectionStr; // Transport-dependent format

        protected P2pNetChannelPeerCollection channelPeers;

        protected Dictionary<string, long> lastMsgIdSent; // last Id sent to each channel. Msg IDs are serial, and PER CHANNEL
        public UniLogger logger;

        //public static long NowMs => DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

        public P2pNetBase(IP2pNetClient _client, string _connectionStr)
        {
             client = _client;
            connectionStr = _connectionStr;
            logger = UniLogger.GetLogger("P2pNet");
            _InitJoinParams();
            localId = _NewP2pId();
        }

        // IP2pNet

        public string GetId() => localId;

        public P2pNetChannel GetMainChannel() =>channelPeers.MainChannel;

        public void Join(P2pNetChannelInfo mainChannelInfo, string localHelloData)
        {
            // The localId "channel" is special - it's not really a P2pNet channel
            // at all: it's just where direct mssages come in, and there's not tracking
            // or timing or anything connected to it. On the other hand - if a message comes in
            // on it from a peer that is already in the channelPeers list (for a "real" channel)
            // then that peer it will get its "heardFrom" property updated.
            _InitJoinParams();
            _Listen(localId); // Listen, but don;t set up a proper "channel"
            _AddChannel(mainChannelInfo, localHelloData ); // Set up channel AND listen
            channelPeers.SetMainChannel( channelPeers.GetChannel(mainChannelInfo.id));
        }

        public void Leave()
        {
            _SendBye(channelPeers.MainChannel.Id);
            _Leave();
            _InitJoinParams();
        }

        public List<string> GetPeerIds() => channelPeers.GetPeerIds();
        public string GetPeerData(string channelId, string peerId) => channelPeers.GetChannelPeer(channelId, peerId)?.helloData;
        public PeerClockSyncData GetPeerClockSyncData(string peerId) => channelPeers.GetPeerClockSyncData(peerId);

        public void Loop()
        {
            if (localId == null)
                return; // Not connected so don't bother

            _Poll(); // Do any network polling

            // TODO: iterating over everything this way is kinda brutish.
            // Ought to be able to figure out when things will need to get done in advance and
            // put them in a priority queue. Then in Loop() just check which need to happen this "fame"
            // and do 'em.

            // During this update we are looking for:
            //
            // - Is this a pch we have never heard from at all? (special case - see AddPeer() )
            //        Should we say hello? SHould we give up on it?
            //
            // - Has this pch timed out?
            //        report it to the client? delete it from the P2pNet lists?
            //        No. Don't delete it from here. Should consider marking it "missing", though.
            //
            // - Do we need send a ping to this pch?
            //      Have we sent something to this pch recently? (do defer the ping)
            //

            // Keep in mind: under the current thinking a message or ping from a peer on one channel
            // DOES count towards "aliveness" on a different channel

            // Hello timeouts
            List<P2pNetChannelPeer> chpsThatFailedHello = channelPeers.ChannelPeers.Values
                .Where( chp => chp.HelloTimedOut()).ToList();
            foreach (P2pNetChannelPeer chp in chpsThatFailedHello)
            {
                logger.Warn($"Loop - Failed HelloTimedOut(). Chp: {SID(chp.P2pId)}/{chp.ChannelId}");
                channelPeers.RemoveChannelPeer(chp); // Just drop it
            }

            // Regular timeouts

            // Not long enough to be dropped - but long enough the app ought to know.
            // "Newly" means notification has not been sent to the client
            List<P2pNetChannelPeer> chpsThatAreNewlyMissing = channelPeers.ChannelPeers.Values
                .Where( chp => chp.IsNewlyMissing() ).ToList();

              foreach (P2pNetChannelPeer chp in chpsThatAreNewlyMissing)
            {
                logger.Warn($"Loop - ChannelPeer {SID(chp.P2pId)}/{chp.ChannelId} is missing. Notifying client.");
                client.OnPeerMissing(chp.ChannelId, chp.P2pId);
                chp.MissingNotificationSent = true; // TODO: find a better way to keep from repeating these messages?
            }

            // Really, really gone. Report 'em and remove 'em
            List<P2pNetChannelPeer> chpsThatHaveTimedOut = channelPeers.ChannelPeers.Values
                .Where( chp => chp.HasTimedOut()).ToList();
            foreach (P2pNetChannelPeer chp in chpsThatHaveTimedOut)
            {
                logger.Warn($"Loop - ChannelPeer {SID(chp.P2pId)}/{chp.ChannelId} timed out. Notifying client and removing peer.");
                client.OnPeerLeft( chp.ChannelId, chp.P2pId);

                channelPeers.RemoveChannelPeer(chp);
            }

            //
            // Who needs a ping?
            //
            // Go through all of the chp's and find all for which (now-lastSentTo <= channel.pingMs)

            List<P2pNetChannelPeer> chpsThatNeedPing = channelPeers.ChannelPeers.Values
                .Where( chp => chp.NeedsPing() ).ToList();

            // filter out chps that we think have dropped (Maybe combine all of this once we've got it down?)
            chpsThatNeedPing = chpsThatNeedPing.Where(chp => !chpsThatHaveTimedOut.Contains(chp)).ToList();  // Slow?

            // What are the channels? How many peers in each?

            // We want a list of (channelId, (peerList)) tuples sorted
            // by peerCount descending...
            List<(string,List<string>)> channelsWithPeers = chpsThatNeedPing.Select(chp => chp.ChannelId).Distinct() // unique channelIds
                    .Select(chId => (chId, chpsThatNeedPing.Where( chp => chp.ChannelId == chId ).Select(chp => chp.P2pId).ToList())) // (chId, List<peerId>),...
                    .OrderByDescending( tup => tup.Item2.Count())                                                   // sorted descendng be peerCnt
                    .ToList();

            // Now, we want that same list - but want for each peerId to only appear once: with the first channel it's listed with.
            // Idea is to group as many peers as possible.
            List<(string,List<string>)> filteredTuples = new List<(string,List<string>)>();
            List<string> usedPeerIds = new List<string>();
            foreach ( (string chId, List<string> peerIds) in channelsWithPeers)
            {
                List<string> remainingPeerIds = peerIds.Where(pid => !usedPeerIds.Contains(pid)).ToList();
                if (remainingPeerIds.Count() > 0)
                {
                    filteredTuples.Add((chId, remainingPeerIds));
                    usedPeerIds.AddRange(peerIds);
                }
            }

            // OK - now for every channel with more than one peer brodcast a ping. For channels with a single peer send directly
            foreach ( (string chId, List<string> peerIds) in filteredTuples)
            {
                if (peerIds.Count() > 1)
                    _SendPing(chId); // broadcast
                else
                    _SendPing(peerIds[0]);
            }

            // After all that... how about clock sync?
            List<P2pNetPeer> peersThatNeedSync = channelPeers.ChannelPeers.Values
                .Where( chp => chp.ClockNeedsSync()).Select(chp => chp.Peer).Distinct().ToList();

            foreach (P2pNetPeer peer in peersThatNeedSync)
                _SendSync(peer.p2pId);

        }

        public void Send(string chanId, string payload)
        {
            if (chanId == localId)
            {
                client.OnClientMsg(localId, localId, 0, payload); // direct loopback
            } else {
                if (channelPeers.IsMainChannel(chanId) || channelPeers.IsKnownChannel(chanId))
                    client.OnClientMsg(localId, chanId, 0, payload); // broadcast channnel loopback

                logger.Debug($"*{SID(localId)}: Send - sending appMsg to {(channelPeers.IsMainChannel(chanId) ? "main channel" : chanId)}");
                _DoSend(chanId, P2pNetMessage.MsgAppl, payload);
            }
        }

        public void AddPeer(string peerId) {} // really only makes sense for direct-connection transports

        public void RemovePeer(string peerId)
        {
            logger.Info($"RemovePeer() Removing: {SID(peerId)}");
            channelPeers.RemovePeer(peerId);
        }

        public void AddSubchannel(P2pNetChannelInfo chan, string localHelloData) => _AddChannel(chan, localHelloData);

        protected void _AddChannel(P2pNetChannelInfo chanInfo, string localHelloData)
        {
            if (channelPeers.AddChannel(chanInfo, localHelloData))
            {
                P2pNetChannel chan = channelPeers.GetChannel(chanInfo.id);
                logger.Info($"Listening to channel: {chanInfo.id}");
                _Listen(chan.Id);
                if (chan.Info.pingMs > 0)
                    _SendHello(chan.Id, chan.Id, true); // broadcast
            }
        }


        public void RemoveSubchannel(string chanId)
        {
            _SendBye(chanId);
            channelPeers.RemoveChannel(chanId);
            _StopListening(chanId);
        }


        // Implementation methods
        protected abstract void _Poll();
        protected abstract void _Join(P2pNetChannelInfo mainChannel);
        protected abstract bool _Send(P2pNetMessage msg);
        protected abstract void _Listen(string channel);
        protected abstract void _StopListening(string channel);
        protected abstract void _Leave();
        protected abstract string _NewP2pId();
        protected abstract void _AddReceiptTimestamp(P2pNetMessage msg);

        // Transport-independent tasks

        protected void _DoSend(string dstChan, string msgType, string payload)
        {
            // Send() is the API for client messages
            long msgId = _NextMsgId(dstChan);
            P2pNetMessage p2pMsg = new P2pNetMessage(dstChan, localId, msgId, msgType, payload);
            p2pMsg.sentTime = P2pNetDateTime.NowMs; // should not happen in ctor
            if (_Send(p2pMsg))
                _UpdateSendStats(dstChan, msgId);
        }

        protected void _OnReceivedNetMessage(string msgChannel, P2pNetMessage msg)
        {
            // NOTE: msgChannel is the channel on which the message was received,
            // which is by definition the same as msg.dstChannel
            // TODO: is the msgChannel param redundant and potentially confusing then?

            // Local messages have already been processed
            if (msg.srcId == localId)
                return; // main channel messages from local peer will show up here

            // If the peer was missing on any channels, inform those channels that it's back BEFORE handling the message
            foreach( P2pNetChannelPeer chp in channelPeers.ChannelPeersForPeer(msg.srcId) )
            {
                if (chp.IsMissing()) // Won't be missing anymore after UnpdateLastHeardFrom() is called for the peer
                    client.OnPeerReturned(chp.ChannelId, chp.P2pId);
                chp.MissingNotificationSent = false; // TODO: find a better way to handle only sending a missing notification once? Maybe?

            }
            channelPeers.GetPeer(msg.srcId)?.UpdateLastHeardFrom(); // Don't need to do this in each handler

            // TODO: get rid of switch
            switch(msg.msgType)
            {
                case P2pNetMessage.MsgAppl:
                    _OnAppMsg(msgChannel, msg);
                    break;
                case P2pNetMessage.MsgHello:
                case P2pNetMessage.MsgHelloReply:
                    _OnHelloMsg(msgChannel, msg);
                    break;
                case P2pNetMessage.MsgGoodbye:
                    _OnByeMsg(msgChannel, msg);
                    break;
                case P2pNetMessage.MsgPing:
                    _OnPingMsg(msgChannel, msg);
                    break;
                case P2pNetMessage.MsgSync:
                    _OnSyncMsg(msg.srcId, msg);
                    break;
            }

            // TODO: should there be a log message if the peer isn't found?

        }

        protected void _OnAppMsg(string msgChanId, P2pNetMessage msg)
        {
            // dispatch a received client-app-specific message
            // Note that this is the only kind of message that gets fielded for non-tracking channels
            P2pNetPeer peer = channelPeers.GetPeer(msg.srcId);

            P2pNetChannel channel = channelPeers.GetChannel(msg.dstChannel); // same as msgChanId
            if (channel == null && msgChanId != localId)
            {
                // I dunno if this can even happen.
                logger.Warn($"_OnAppMsg - Unknown channel: {msg.dstChannel}");
                return;
            }

            if (peer == null && channel?.Info.pingMs == 0) // unknown peer, non-tracking channel
            {
                // Add the channelPeer pair - the channel in non-tracking so there won;t be pings and it might time out
                logger.Debug($"_OnAppMsg - Adding unknown peer {SID(msg.srcId) } sending for non-tracking channel {channel.Id}");
                channelPeers.AddChannelPeer(channel.Id, msg.srcId); // add the channel/peer pair
                peer = channelPeers.GetPeer(msg.srcId);
            }

            if (peer != null)
            {
                // I there's clock sync for the channel the figure out when the msg was sent
                long realMsSinceSend = -1; // means no clock sync
                if (channel != null && channel.IsSyncingClocks)
                {
                    long remoteMsNow = P2pNetDateTime.NowMs + peer.ClockOffsetMs;
                    realMsSinceSend =  remoteMsNow - msg.sentTime;
                    if (realMsSinceSend < 0)
                    {
                        logger.Debug($"_OnAppMsg() msg from { SID(msg.srcId)} w/lag < 0: {realMsSinceSend}");
                        realMsSinceSend = 0;
                    }
                }

                logger.Debug($"_OnAppMsg - msg from {SID(msg.srcId)}" );
                client.OnClientMsg(msg.srcId, msg.dstChannel, realMsSinceSend, msg.payload);

            } else {
                logger.Warn($"_OnAppMsg - Unknown peer {SID(msg.srcId)} sending on channel {msgChanId} for channel {msg.dstChannel}");
            }
        }

        protected void _InitJoinParams()
        {
            channelPeers = new P2pNetChannelPeerCollection();
            lastMsgIdSent = new Dictionary<string, long>();
        }

        protected long _NextMsgId(string chan)
        {
            try {
                return lastMsgIdSent[chan] + 1;
            } catch (KeyNotFoundException){
                return 1;
            }
        }

        protected void _UpdateSendStats(string chanId, long latestMsgId)
        {
            // Figure out who is actually getting this message and update the "last time we sent anything to them" property.
            // Note that this is per peer - sending to a peer on one channel DOES update if we think we need to ping on another channel.

            lastMsgIdSent[chanId] = latestMsgId; // note that this DOES include direct message "channels"

            P2pNetChannelInfo chanData = channelPeers.GetChannel(chanId)?.Info;

            if (chanData == null) // it was sent directly to a peer
            {
                channelPeers.GetPeer(chanId)?.UpdateLastSentTo();
            }
            else
            {
                if (chanData.pingMs > 0) // it's a tracking channel
                {
                    foreach (P2pNetPeer p in channelPeers.PeersForChannel(chanId))
                        p.UpdateLastSentTo(); // so we don't ping until it's needed
                }
            }
        }

        // Some specific messages

        protected void _SendHello(string destChannel, string subjectChannel, bool requestReply)
        {
            // When joining a new channel, destChannel and subjectChannel are typically the same.
            // When replying, or sending to a single peer, the destChannel is usually the recipient peer
            P2pNetChannel chan = channelPeers.GetChannel(subjectChannel);
            string msgType = requestReply ?  P2pNetMessage.MsgHello : P2pNetMessage.MsgHelloReply;
            _DoSend(destChannel, msgType, JsonConvert.SerializeObject(new HelloPayload(subjectChannel, chan.LocalHelloData)));
        }

        protected void _OnHelloMsg(string unusedSrcChannel, P2pNetMessage msg)
        {
            HelloPayload hp = JsonConvert.DeserializeObject<HelloPayload>(msg.payload);
            string senderId = msg.srcId;

            P2pNetChannelPeer chp = channelPeers.GetChannelPeer(hp.channelId, senderId);
            if (chp == null)
                chp = channelPeers.AddChannelPeer(hp.channelId, senderId);

            if (chp.helloData == null)
            {
                logger.Verbose($"_OnHelloMsg - Hello for channel {chp.ChannelId} from peer {SID(chp.P2pId)}" );

                chp.helloData = hp.channelHelloData;
                chp.Peer.UpdateLastHeardFrom();
                if ( msg.msgType == P2pNetMessage.MsgHello)
                {
                    logger.Verbose($"_OnHelloMsg - replying directly to {SID(chp.P2pId)} about channel {chp.ChannelId}");
                    _SendHello(chp.P2pId, chp.ChannelId, false); // we don;t want a reply
                }
                logger.Verbose($"_OnHelloMsg - calling client.");
                client.OnPeerJoined(chp.ChannelId, chp.P2pId, hp.channelHelloData);
            }
        }

        protected void _SendPing(string chanId)
        {
            // FIXME: check about this "everyone" stuff. Not sure if it's a thing anymore?
            // FIXME: actually: check about pinging in general: should we be doing something akin to what we used to do:
            //     if there were more than one (or 2) peers that needed a ping we bcast it...
            // Would that make sense with ChannelPeers, too?

            string toWhom = channelPeers.IsMainChannel(chanId) ? "Everyone" : SID(chanId);
            logger.Verbose(string.Format($"_SendPing() - Sending to {toWhom} on ch: {chanId}" ));
            _DoSend(chanId, P2pNetMessage.MsgPing, null);
        }

        protected void _OnPingMsg(string srcChannel, P2pNetMessage msg)
        {
            logger.Verbose($"*{SID(localId)}: _OnPingMsg - Ping from {SID(msg.srcId)}");
            // Don't really do anything. We already called updateLastHeardFrom for the peer
        }

        protected void _SendBye(string chanId)
        {
            _DoSend(chanId, P2pNetMessage.MsgGoodbye, null);
        }

        protected void _OnByeMsg(string srcChannel, P2pNetMessage msg)
        {
            channelPeers.RemoveChannelPeer(srcChannel, msg.srcId);

            if (srcChannel == channelPeers.MainChannel.Id)
            {
                client.OnPeerLeft(srcChannel, msg.srcId);
                channelPeers.RemovePeer(msg.srcId);
            }
        }

        // NOTE: sync packets use the actual message timestamps. So, for insntance, when the
        // first Sync is sent t0 is NOT set - it gets set by the recipient from the
        // sentTime field. Liekwise, when it gets to _OnSynMsg, the receipt t1 or t3
        // is set from the incoming msg rcvdTime field.
        protected void _SendSync(string dest, SyncPayload _payload=null)
        {
            SyncPayload payload = _payload ?? new SyncPayload();
            P2pNetPeer peer = channelPeers.GetPeer(dest);
            if (peer != null)   // seen it happen
            {
                peer.ReportSyncProgress();
                // payload "sent time" gets set by receiver.
                _DoSend(dest, P2pNetMessage.MsgSync, JsonConvert.SerializeObject(payload));
            }
        }

        protected void _OnSyncMsg(string from, P2pNetMessage msg)
        {
             P2pNetPeer peer = channelPeers.GetPeer(from);
            if (peer != null)
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
                    logger.Info($"Synced (org) {SID(from) } Lag: {peer.NetworkLagMs}, Offset: {peer.ClockOffsetMs}");
                    foreach (P2pNetChannel ch in channelPeers.ChannelsForPeer(peer.p2pId))
                    {
                        if (ch.IsSyncingClocks)
                            client.OnPeerSync(ch.Id,peer.p2pId, peer.ClockOffsetMs, peer.NetworkLagMs);
                    }

               } else {
                    // we're the recipient and it's done
                    peer.UpdateClockSync(payload.t2, payload.t3, msg.sentTime, msg.rcptTime);
                    logger.Info($"Synced (rcp) {SID(from)} Lag: {peer.NetworkLagMs}, Offset: {peer.ClockOffsetMs}");
                    // TODO: fix the following copypasta
                    foreach (P2pNetChannel ch in channelPeers.ChannelsForPeer(peer.p2pId))
                    {
                        if (ch.IsSyncingClocks)
                            client.OnPeerSync(ch.Id,peer.p2pId, peer.ClockOffsetMs, peer.NetworkLagMs);
                    }
                }
            } else {
               logger.Warn($"Got sync from unknown peer: {SID(from)}. Ignoring.");
            }
        }

    }
}
