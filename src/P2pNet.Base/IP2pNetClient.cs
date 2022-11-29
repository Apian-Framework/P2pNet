namespace P2pNet
{
    public interface IP2pNetClient
    {
        void OnPeerJoined(string channel, string peerAddr, string helloData);
        void OnPeerMissing(string channel, string peerAddr);
        void OnPeerReturned(string channel, string peerAddr);
        void OnPeerLeft(string channel, string peerAddr);
        void OnPeerSync(string channel, string peerAddr, PeerClockSyncInfo syncInfo);
        void OnClientMsg(string from, string toChan, long msSinceSent, string payload);
    }
}