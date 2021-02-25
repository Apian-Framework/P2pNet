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
        public long dropMs;
        public long pingMs; // 0 means "don't track". No hello, no pings, no drop, no timing.
        public long timingMs; // zero means "don;t compute net timing stats" - meaningless if pingMs is 0.
        public int maxPeers;  // 0 means no max
        public P2pNetChannelInfo(string _name, string _id, long _dropMs, long _pingMs=0,  long _timingMs=0, int _maxPeers=0)
        {
            name = _name;
            id = _id;
            dropMs = _dropMs;
            pingMs = _pingMs;
            timingMs = _timingMs;
            maxPeers = _maxPeers;
        }
    }

    // public class P2pNetChannel
    // {
    //     public P2pNetChannelInfo Info { get; private set;}

    //     public P2pNetChannel(P2pNetChannelInfo info)
    //     {
    //         Info = info;
    //     }
    // }



}
