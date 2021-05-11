using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UniLog;

namespace P2pNet
{

    public class P2pNetChannelPeer
    {
        // This is a Channel/Peer pairing.
        // P2pNet in most ways deals with channels as individual "networks" for things like
        // membership tracking, and so things like dscovery (helloData) and drop timing (and pings) are
        // done per "ChannelPeer" rather than per peer.
        // So state data that might otherwise be expected to be per peer, lives here instead.
        //
        // Note that this is NOT the case for the NTP-style network timing estimates. They are really per-peer.

        public P2pNetChannel Channel { get; private set; }
        public P2pNetPeer Peer { get; private set; }
        public string helloData;
        protected long firstHelloSentTs = 0; // when did we FIRST send a hello/hello req? (for knowing when to give up)
        // TODO: need to set the above either in the constructor (if it includes hello data)
        // or when we send a hello to a peer that has firstHelloSentTs == 0;
        protected long lastMsgId = 0; // Last msg rcvd from this channelPeer. Each tags each mesage with a serial # (nextMsgId in P2PNetBase)

        public P2pNetChannelPeer(P2pNetPeer peer, P2pNetChannel channel)
        {
            Channel = channel;
            Peer = peer;
        }

        public string P2pId { get => Peer.p2pId;}
        public string ChannelId { get => Channel.Id;}

        public bool HaveTriedToContact() => firstHelloSentTs > 0;

        public bool HaveHeardFrom() => helloData != null;

        public bool WeShouldSendHello()
        {
            // Should we send hello to a node we've never heard from?
            // Yes if:
            // - We have never gotten a hello from them  AND
            //      - If we've never sent a hello.
            //      OR
            //      - it's been more that a ping-time since we did. (retry)
            return Channel.IsTrackingMemberShip
                && !HaveHeardFrom()
                && ( !HaveTriedToContact()
                  || (P2pNetDateTime.NowMs - Peer.LastHeardFromTs > Channel.Info.pingMs)
                );
        }

        public bool HelloTimedOut()
        {
            return !HaveHeardFrom()
                && HaveTriedToContact()
                && (P2pNetDateTime.NowMs - firstHelloSentTs > Channel.Info.dropMs);
        }

        public bool IsMissing() // Keep in mind: "missing" is per-channel/peer
        {
            return HaveHeardFrom()
                && Channel.IsTrackingMemberShip  // must be tracking/pinging
                && Channel.ReportsMissingPeers
                && P2pNetDateTime.NowMs - Peer.LastHeardFromTs > Channel.Info.missingMs;
        }

        public bool MissingNotificationSent {get; set;}

        public bool IsNewlyMissing()
        {
            return IsMissing() && !MissingNotificationSent;
        }

        public bool HasTimedOut()
        {
            return HaveHeardFrom()
                &&  Channel.Info.dropMs > 0
                && P2pNetDateTime.NowMs - Peer.LastHeardFromTs > Channel.Info.dropMs;
        }

        public void UpdateLastSentTo() =>  Peer.UpdateLastSentTo();

        public bool NeedsPing() => (Channel.Info.pingMs > 0) && (P2pNetDateTime.NowMs - Peer.LastSentToTs) > Channel.Info.pingMs;

        public bool ClockNeedsSync() => (Channel.Info.netSyncMs> 0 ) && Peer.ClockNeedsSync(Channel.Info.netSyncMs);

        public bool ValidateMsgId(long msgId)
        {
            // reject any new msg w/ id <= what we have already seen
            if (msgId <= lastMsgId)
                return false;
            lastMsgId = msgId;
            return true;
        }

    }

    public class P2pNetChannelPeerCollection // TODO: needs a better name
    {
        public  P2pNetChannel MainChannel { get; private set;}  // reference into Channels
        public Dictionary<string, P2pNetPeer> Peers { get; protected set; }
        public Dictionary<string, P2pNetChannel> Channels; //
        public Dictionary<string, P2pNetChannelPeer> ChannelPeers { get; private set; } // key is (<channelID>/<peerId>)
        UniLogger logger;

        public P2pNetChannelPeerCollection()
        {
            Peers = new Dictionary<string, P2pNetPeer>();
            Channels = new Dictionary<string, P2pNetChannel>();
            ChannelPeers = new Dictionary<string, P2pNetChannelPeer>();
            logger = UniLogger.GetLogger("P2pNet");
        }

        public void SetMainChannel(P2pNetChannel chan) => MainChannel = chan;

        // ChannelPeer Ops
        public static string ChannelPeerKey(string channelId, string peerId) =>  $"{peerId}/{channelId}";
        public static string ChannelPeerKey(P2pNetChannelPeer chp) => ChannelPeerKey(chp.Channel.Id, chp.Peer.p2pId);

        public P2pNetChannelPeer GetChannelPeer(string channelPeerKey)
        {
            return ChannelPeers.ContainsKey(channelPeerKey) ? ChannelPeers[channelPeerKey] : null;
        }
        public P2pNetChannelPeer GetChannelPeer(string chanlId, string peerId) => GetChannelPeer( ChannelPeerKey(chanlId, peerId));

        public P2pNetChannelPeer AddChannelPeer(string chanId, string peerId)
        {
            P2pNetChannel channel = GetChannel(chanId);
            if (channel == null)
            {
                logger.Warn($"AddChannelPeer() - Unknown channel: {chanId}");
                return null;
            }

            string chpKey = ChannelPeerKey(chanId, peerId);
            if (ChannelPeers.ContainsKey(chpKey))
            {
                logger.Warn($"AddChannelPeer() - channelPeer exists: {chpKey}"); // Warning? Maybe just Info?
                return ChannelPeers[chpKey]; // exists - Or should we return null and fail?
            }

            P2pNetPeer peer = GetPeer(peerId) ?? AddPeer(new P2pNetPeer(peerId));
            ChannelPeers[chpKey] = new P2pNetChannelPeer(peer, channel);
            logger.Info($"AddChannelPeer() - Added: {channel.Id}/{peerId}");
            return ChannelPeers[chpKey];
        }
        public P2pNetChannelPeer AddChannelPeer(P2pNetChannelInfo chan, string peerId) => AddChannelPeer(chan.id, peerId);

        public void RemoveChannelPeer(P2pNetChannelPeer chp)
        {
            string chpKey = ChannelPeerKey(chp);
            string peerId = chp.Peer.p2pId;
            if (ChannelPeers.ContainsKey(chpKey))
            {
                ChannelPeers.Remove(chpKey);
            }

            // TODO: if the channel is "mainchannel" should we remove the peer and all other channelPeers it's in?
            if (ChannelsForPeer(peerId).Count == 0)
            {
                Peers.Remove(peerId);
            }

        }

        public void RemoveChannelPeer(string chanId, string peerId)
        {
            string chpKey = ChannelPeerKey(chanId, peerId);
            if (ChannelPeers.ContainsKey(chpKey))
                ChannelPeers.Remove(chpKey);
        }

        public List<P2pNetPeer> PeersForChannel(string chanId) => ChannelPeers.Values.Where(cp => cp.Channel.Id == chanId).Select(cp => cp.Peer).ToList();
        public List<P2pNetChannel> ChannelsForPeer(string peerId) => ChannelPeers.Values.Where(cp => cp.Peer.p2pId == peerId).Select(cp => cp.Channel).ToList();
        public List<P2pNetChannelPeer> ChannelPeersForPeer(string peerId) => ChannelPeers.Values.Where(cp => cp.P2pId == peerId).ToList();
        public List<string> CpKeysForChannel(string chanId) => ChannelPeers.Where(kvp => kvp.Value.Channel.Id == chanId).Select(kvp => kvp.Key).ToList();

        // Peer stuff
        public bool IsKnownPeer(string peerId) => Peers.ContainsKey(peerId);
        public P2pNetPeer GetPeer(string peerId)
        {
            return Peers.ContainsKey(peerId) ? Peers[peerId] : null;
        }
        private P2pNetPeer AddPeer(P2pNetPeer peer)
        {
            Peers[peer.p2pId] = peer;
            return peer;
        }
        public bool RemovePeer(string peerId)
        {
            if (IsKnownPeer(peerId))
            {
                List<P2pNetChannelPeer> cpsToRemove = ChannelPeers.Values.Where(cp => cp.Peer.p2pId == peerId).ToList();
                foreach (P2pNetChannelPeer c in cpsToRemove)
                    RemoveChannelPeer(c);

                if (Peers.Keys.Contains(peerId)) // should be gone after the above
                    Peers.Remove(peerId);
                return true;
            }
            return false;
        }

        public List<string> GetPeerIds() => Peers.Keys.ToList();
        public PeerClockSyncData GetPeerClockSyncData(string peerId)
        {
            try {
                P2pNetPeer p = Peers[peerId];
                return new PeerClockSyncData(p.p2pId, p.MsSinceClockSync, p.ClockOffsetMs, p.NetworkLagMs);
            } catch(KeyNotFoundException) {
                return null;
            }
        }

        public P2pNetChannel GetChannel(string chanId)
        {
            return Channels.ContainsKey(chanId) ? Channels[chanId] : null;
        }
        public bool IsKnownChannel(string channelId) => Channels.ContainsKey(channelId);
        public bool IsMainChannel(string chanId) => chanId == MainChannel?.Id;


        public bool AddChannel(P2pNetChannelInfo chan, string localHelloData)
        {
            if (localHelloData == null)
                throw( new Exception($"P2pNetChannelPeer.AddChannel(): local channel HelloData cannot be null. Channel: {chan.id}"));

            if (!Channels.Keys.Contains(chan.id))
            {
                Channels[chan.id] = new P2pNetChannel(chan, localHelloData);
                return true;
            }
            logger.Warn($"Channel already exists: {chan.id}");
            return false;
        }
        public bool RemoveChannel(string chanId)
        {
            if (chanId == MainChannel?.Id)
                logger.Error("RemoveChannel() - Can;t remove main channel");
            else if (Channels.Keys.Contains(chanId))
            {
                foreach( string id in CpKeysForChannel(chanId))
                    ChannelPeers.Remove(id);
                Channels.Remove(chanId);
                return true;
            }
            return false;
        }

    }


}
