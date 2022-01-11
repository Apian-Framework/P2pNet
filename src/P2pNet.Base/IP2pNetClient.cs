namespace P2pNet
{
    public interface IP2pNetClient
    {
        void OnPeerJoined(string channel, string p2pId, string helloData);
        void OnPeerMissing(string channel, string p2pId);
        void OnPeerReturned(string channel, string p2pId);
        void OnPeerLeft(string channel, string p2pId);
        void OnPeerSync(string channel, string p2pId, PeerClockSyncInfo syncInfo);
        void OnClientMsg(string from, string toChan, long msSinceSent, string payload);
    }
}