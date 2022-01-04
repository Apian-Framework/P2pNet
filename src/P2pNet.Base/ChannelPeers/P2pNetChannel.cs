using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UniLog;

namespace P2pNet
{
    public class P2pNetChannel
    {
        public P2pNetChannelInfo Info { get; private set;}
        public string LocalHelloData { get; private set; }
        public string Id { get => Info.id; }
        public string Name { get => Info.name; }

        public bool IsTrackingMemberShip { get => Info.pingMs > 0;}
        public bool ReportsMissingPeers { get => Info.missingMs > 0; }
        public bool IsSyncingClocks { get => Info.netSyncMs > 0 && Info.pingMs > 0; }

        public P2pNetChannel(P2pNetChannelInfo info, string localHelloData)
        {
            Info = info;
            LocalHelloData = localHelloData;
        }
    }

}
