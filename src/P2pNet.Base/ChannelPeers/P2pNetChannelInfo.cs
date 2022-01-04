using System;

namespace P2pNet
{
    public class P2pNetChannelInfo
    {
        public string name;
        public string id;
        public int dropMs;
        public int pingMs; // 0 means "don't track". No hello, no pings, no drop, no timing.
        public int missingMs; // 0 means "don't report peers as mising". Idea is that an app might not want a peer dropped from
            // the entire network if it has just kinda hung for a few seconds - but the app might want to be notified.
        public int netSyncMs; // zero means "don't compute net timing stats" - meaningless if pingMs is 0.
        public int maxPeers;  // 0 means no max
        public P2pNetChannelInfo(string _name, string _id, int _dropMs, int _pingMs=0,  int _missingMs = 0, int _netSyncMs=0, int _maxPeers=0)
        {
            name = _name;
            id = _id;
            dropMs = _dropMs;
            pingMs = _pingMs;
            missingMs = _missingMs;
            netSyncMs = _netSyncMs;
            maxPeers = _maxPeers;
        }
        public P2pNetChannelInfo(P2pNetChannelInfo inf)
        {
            name = inf.name;
            id = inf.id;
            dropMs = inf.dropMs;
            pingMs = inf.pingMs;
            missingMs = inf.missingMs;
            netSyncMs = inf.netSyncMs;
            maxPeers = inf.maxPeers;
        }

        public P2pNetChannelInfo() {}

        public bool IsEquivalentTo(P2pNetChannelInfo inf2)
        {
            return (name.Equals(inf2.name, StringComparison.Ordinal)
                && id.Equals(inf2.id, StringComparison.Ordinal)
                && dropMs == inf2.dropMs
                && pingMs == inf2.pingMs
                && missingMs == inf2.missingMs
                && netSyncMs == inf2.netSyncMs
                && maxPeers == inf2.maxPeers );
        }

    }
}