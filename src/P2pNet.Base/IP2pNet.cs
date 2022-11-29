using System.Collections.Generic;

namespace P2pNet
{
    public interface IP2pNet
    {
        // ReSharper disable UnusedMember.Global
        void Update(); // needs to be called periodically (drives message pump + group handling)
        string LocalAddress { get; } // Local peer's Address
        P2pNetChannel GetNetworkChannel();
        void Join(P2pNetChannelInfo mainChannel, string helloData);
        void AddSubchannel(P2pNetChannelInfo subChannel, string helloData);
        void RemoveSubchannel(string subChannelId);
        List<string> GetPeerAddrs();
        string GetPeerData(string channelId, string peerAddr); // Remote peer's HELLO data
        PeerClockSyncInfo GetPeerClockSyncData(string peerAddr);
        void Leave();
        void Send(string dest, string payload); // dest can be peer address or channel id (peer Id works, too, since its a channel id)
        // void AddPeer(string peerId); // These might be needed for non-pubsub point-to-pont networking, but I'm not quite sure how it should work...
        // void RemovePeer(string peerId); // So I'm going to wait until something needs it before implmenting anything
    }

    public interface IP2pNetBase // carrier protocol uses this
    {
        string LocalId { get; } // Local peer's P2pNet ID.
        void SendHelloMsg(string destChannel, string subjectChannel, string helloMsgType = P2pNetMessage.MsgHello); // might be needed
        void OnNetworkJoined(P2pNetChannelInfo mainChannelInfo, string localHelloData);
        void OnReceivedNetMessage(string msgChannel, P2pNetMessage msg);

    }
}