using System;
using System.Collections.Generic;
using System.Linq;
using UniLog;

namespace P2pNet
{
    // TODO: This is a terrible name, but the only other thing I could think of at the time was
    // P2pNetChannelPeerCollection - which is kinda what it is, but the word Collection has a pretty
    // specific meaning that isn't what this is.

    public class P2pNetChannelPeerPairings
    {
        public  P2pNetChannel MainChannel { get; private set;}  // reference into Channels
        public Dictionary<string, P2pNetPeer> PeersById { get; private set; }
        public Dictionary<string, P2pNetPeer> PeersByAddress { get; private set; }
        public Dictionary<string, P2pNetChannel> Channels { get; private set; } //
        public Dictionary<string, P2pNetChannelPeer> ChannelPeers { get; private set; } // key is (<channelID>/<peerId>)
        public UniLogger Logger;

        public P2pNetChannelPeerPairings()
        {
            PeersById = new Dictionary<string, P2pNetPeer>();
            PeersByAddress = new Dictionary<string, P2pNetPeer>();
            Channels = new Dictionary<string, P2pNetChannel>();
            ChannelPeers = new Dictionary<string, P2pNetChannelPeer>();
            Logger = UniLogger.GetLogger("P2pNet");
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

        public P2pNetChannelPeer GetChannelPeerByAddress(string chanlId, string peerAddr)
        {
            P2pNetPeer peer = PeersByAddress[peerAddr];
            return peer != null ? GetChannelPeer( ChannelPeerKey(chanlId, peer.p2pId)) : null;
        }

        public P2pNetChannelPeer AddChannelPeer(string chanId, string peerId)
        {
            P2pNetChannel channel = GetChannel(chanId);
            if (channel == null)
            {
                Logger.Warn($"AddChannelPeer() - Unknown channel: {chanId}");
                return null;
            }

            string chpKey = ChannelPeerKey(chanId, peerId);
            if (ChannelPeers.ContainsKey(chpKey))
            {
                Logger.Warn($"AddChannelPeer() - channelPeer exists: {chpKey}"); // Warning? Maybe just Info?
                return ChannelPeers[chpKey]; // exists - Or should we return null and fail?
            }

            P2pNetPeer peer = GetPeerById(peerId) ?? _AddPeer(new P2pNetPeer(peerId));
            ChannelPeers[chpKey] = new P2pNetChannelPeer(peer, channel);
            Logger.Info($"AddChannelPeer() - Added: {channel.Id}/{peerId}");
            return ChannelPeers[chpKey];
        }
        public P2pNetChannelPeer AddChannelPeer(P2pNetChannelInfo chan, string peerId) => AddChannelPeer(chan.id, peerId);

        public void RemoveChannelPeer(P2pNetChannelPeer chp)
        {
            string chpKey = ChannelPeerKey(chp);
            string peerId = chp.Peer.p2pId;
            string peerAddr = chp.Peer.p2pAddress;
            if (ChannelPeers.ContainsKey(chpKey))
            {
                ChannelPeers.Remove(chpKey);
            }

            // TODO: if the channel is "mainchannel" should we remove the peer and all other channelPeers it's in?
            if (ChannelsForPeer(peerId).Count == 0)
            {
                PeersByAddress.Remove(peerAddr);
                PeersById.Remove(peerId);
            }

        }

        public bool RemoveChannelPeer(string chanId, string peerId)
        {
            string chpKey = ChannelPeerKey(chanId, peerId);
            if (ChannelPeers.ContainsKey(chpKey))
            {
                ChannelPeers.Remove(chpKey);
                // If the peer is now in no channels, remove it.
                if (ChannelsForPeer(peerId).Count == 0)
                    RemovePeer(peerId);
                return true;
            }
            return false;
        }

        public List<P2pNetPeer> PeersForChannel(string chanId) => ChannelPeers.Values.Where(cp => cp.Channel.Id == chanId).Select(cp => cp.Peer).ToList();
        public List<P2pNetChannel> ChannelsForPeer(string peerId) => ChannelPeers.Values.Where(cp => cp.Peer.p2pId == peerId).Select(cp => cp.Channel).ToList();
        public List<P2pNetChannelPeer> ChannelPeersForPeer(string peerId) => ChannelPeers.Values.Where(cp => cp.P2pId == peerId).ToList();
        public List<string> CpKeysForChannel(string chanId) => ChannelPeers.Where(kvp => kvp.Value.Channel.Id == chanId).Select(kvp => kvp.Key).ToList();

         // Peer stuff
        public bool IsKnownPeer(string peerId) => PeersById.ContainsKey(peerId);

        public P2pNetPeer GetPeerById(string peerId)
        {
            return PeersById.ContainsKey(peerId) ? PeersById[peerId] : null;
        }

        public P2pNetPeer GetPeerByAddress(string peerAddr)
        {
            return PeersByAddress.ContainsKey(peerAddr) ? PeersByAddress[peerAddr] : null;
        }

        private P2pNetPeer _AddPeer(P2pNetPeer peer)
        {
            PeersById[peer.p2pId] = peer;
            // we don;t know peer address yet
            return peer;
        }

        public void UpdatePeerAddress(P2pNetPeer peer, string address)
        {
            peer.SetAddress(address); //
            PeersByAddress[address] = peer;
        }


        public bool RemovePeer(string peerId)
        {
            if (IsKnownPeer(peerId))
            {
                string peerAddr = GetPeerById(peerId).p2pAddress;
                List<P2pNetChannelPeer> cpsToRemove = ChannelPeers.Values.Where(cp => cp.Peer.p2pId == peerId).ToList();
                foreach (P2pNetChannelPeer c in cpsToRemove)
                    RemoveChannelPeer(c);

               if (PeersByAddress.ContainsKey(peerAddr))
                    PeersByAddress.Remove(peerAddr);

                if (PeersById.ContainsKey(peerId)) // should be gone after the above
                    PeersById.Remove(peerId);
                return true;
            }
            return false;
        }

        public List<string> GetPeerAddrs() => PeersById.Values.Where( (p) => p.p2pAddress != null).Select( (p) => p.p2pAddress).ToList();

        public PeerClockSyncInfo GetPeerClockSyncDataByAddress(string peerAddr)
        {
            try {
                return PeersByAddress[peerAddr].ClockSyncInfo;
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

            if (!Channels.ContainsKey(chan.id))
            {
                Channels[chan.id] = new P2pNetChannel(chan, localHelloData);
                return true;
            }
            Logger.Warn($"Channel already exists: {chan.id}");
            return false;
        }
        public bool RemoveChannel(string chanId)
        {
            if (chanId == MainChannel?.Id)
                Logger.Warn("RemoveChannel() - Can't remove main channel");
            else if (Channels.ContainsKey(chanId))
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
