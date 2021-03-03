using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UniLog;

namespace P2pNet
{

    public class P2pNetChannelInfo
    {
        public string name;
        public string id;
        public int dropMs;
        public int pingMs; // 0 means "don't track". No hello, no pings, no drop, no timing.
        public int netSyncMs; // zero means "don't compute net timing stats" - meaningless if pingMs is 0.
        public int maxPeers;  // 0 means no max
        public P2pNetChannelInfo(string _name, string _id, int _dropMs, int _pingMs=0,  int _netSyncMs=0, int _maxPeers=0)
        {
            name = _name;
            id = _id;
            dropMs = _dropMs;
            pingMs = _pingMs;
            netSyncMs = _netSyncMs;
            maxPeers = _maxPeers;
        }
    }

    public class P2pNetChannel
    {
        public P2pNetChannelInfo Info { get; private set;}
        public string LocalHelloData { get; private set; }
        public string Id { get => Info.id; }
        public string Name { get => Info.name; }

        public bool IsTrackingMemberShip { get => Info.pingMs > 0;}
        public bool IsSyncingClocks { get => Info.netSyncMs > 0 && Info.pingMs > 0; }

        public P2pNetChannel(P2pNetChannelInfo info, string localHelloData)
        {
            Info = info;
            LocalHelloData = localHelloData;
        }
    }



}
