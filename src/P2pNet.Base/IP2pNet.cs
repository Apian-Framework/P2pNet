using System.Collections.Generic;

namespace P2pNet
{
    public interface IP2pNet
    {
        // ReSharper disable UnusedMember.Global
        void Update(); // needs to be called periodically (drives message pump + group handling)
        string GetId(); // Local peer's P2pNet ID.
        P2pNetChannel GetMainChannel();
        void Join(P2pNetChannelInfo mainChannel, string helloData);
        void AddSubchannel(P2pNetChannelInfo subChannel, string helloData);
        void RemoveSubchannel(string subChannelId);
        List<string> GetPeerIds();
        string GetPeerData(string channelId, string peerId); // Remote peer's HELLO data
        P2pNetClockSyncData GetPeerClockSyncData(string peerId);
        void Leave();
        void Send(string chan, string payload);
        void AddPeer(string peerId);
        void RemovePeer(string peerId);
    }
}