using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UniLog;

namespace P2pNet
{

    public class P2pNetChannelPeer
    {
        public P2pNetChannelInfo Channel { get; private set; }
        public P2pNetPeer Peer { get; private set; }

    }

    public class P2pNetChannelPeers
    {
        // In many (most?) cases the combination of a channel and a peer are treated as a unit

        public  P2pNetChannelInfo MainChannel { get; private set;}
        public Dictionary<string, P2pNetPeer> Peers { get; protected set; }
        public Dictionary<string, P2pNetChannelInfo> subChannels; // other non-peer channels we are using

        public P2pNetChannelPeers(P2pNetChannelInfo mainCh)
        {
            MainChannel = mainCh;
            Peers = new Dictionary<string, P2pNetPeer>();
            subChannels = new Dictionary<string, P2pNetChannelInfo>();
        }

        // Peer stuff
        public bool IsKnownPeer(string peerId) => Peers.ContainsKey(peerId);
        public P2pNetPeer GetPeer(string peerId)
        {
            return Peers.ContainsKey(peerId) ? Peers[peerId] : null;
        }
        public void TempAddPeer(P2pNetPeer peer) // FIXME: delete this func
        {
            Peers[peer.p2pId] = peer;
        }
        public bool RemovePeer(string peerId)
        {
            if (IsKnownPeer(peerId))
            {
                // FIXME: remove any P2pNetChannelPeers &&&&&&
                Peers.Remove(peerId);
                return true;
            }
            return false;
        }

        public List<string> GetPeerIds() => Peers.Keys.ToList();
        public string GetPeerData(string peerId)
        {
            try {
                return Peers[peerId].helloData;
            } catch(KeyNotFoundException) {
                return null;
            }
        }
        public PeerClockSyncData GetPeerClockSyncData(string peerId)
        {
            try {
                P2pNetPeer p = Peers[peerId];
                return new PeerClockSyncData(p.p2pId, p.MsSinceClockSync, p.ClockOffsetMs, p.NetworkLagMs);
            } catch(KeyNotFoundException) {
                return null;
            }
        }

        public bool IsKnownChannel(string channelId) => subChannels.ContainsKey(channelId);
        public bool IsMainChannel(string chanId) => chanId == MainChannel?.id;


        public bool AddSubchannel(P2pNetChannelInfo chan)
        {
            if (!subChannels.Keys.Contains(chan.id))
            {
                subChannels[chan.id] = chan;
                return true;
            }
            return false;
        }
        public bool RemoveSubchannel(string chanId)
        {
            if (subChannels.Keys.Contains(chanId))
            {
                subChannels.Remove(chanId);
                return true;
            }
            return false;
        }

    }


}
