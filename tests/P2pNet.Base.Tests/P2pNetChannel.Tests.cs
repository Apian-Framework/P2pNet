using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Newtonsoft.Json;
using P2pNet;
using UniLog;

namespace P2pNetBaseTests
{
    [TestFixture]
    public class P2pnetChannelInfoTests
    {
        [Test]
        public void P2pNetChannelInfo_ConstructorWorks()
        {
            // public P2pNetChannelInfo(string _name, string _id, int _dropMs, int _pingMs=0,  int _netSyncMs=0, int _maxPeers=0)
            const string name = "theChannel",
                    id = "theChannelId";

            const int dropMs = 10000,
                pingMs = 3000,
                missingMs = 5000,
                netSyncMs = 12000,
                maxPeers = 50;

            P2pNetChannelInfo chanInfo = new P2pNetChannelInfo( name, id, dropMs, pingMs, missingMs, netSyncMs, maxPeers);
            Assert.That(chanInfo, Is.Not.Null);
            Assert.That(chanInfo.name, Is.EqualTo(name));
            Assert.That(chanInfo.id, Is.EqualTo(id));
            Assert.That(chanInfo.pingMs, Is.EqualTo(pingMs));
            Assert.That(chanInfo.missingMs, Is.EqualTo(missingMs));
            Assert.That(chanInfo.netSyncMs, Is.EqualTo(netSyncMs));
            Assert.That(chanInfo.maxPeers, Is.EqualTo(maxPeers));
        }
    }

    [TestFixture]
    public class P2pnetChannelTests
    {
        [Test]
        public void P2pNetChannel_ConstructorWorks()
        {
            const string name = "theChannel",
                    id = "theChannelId";
            const int dropMs = 10000,
                pingMs = 3000,
                missingMs = 5000,
                netSyncMs = 12000,
                maxPeers = 50;

            const string localHelloData = "This is the local hello data";

            // public P2pNetChannel(P2pNetChannelInfo info, string localHelloData)
            P2pNetChannelInfo chanInfo = new P2pNetChannelInfo( name, id, dropMs, pingMs, missingMs, netSyncMs, maxPeers);

            P2pNetChannel chan = new P2pNetChannel(chanInfo, localHelloData);
            Assert.That(chan, Is.Not.Null);
            Assert.That(chan.Info, Is.EqualTo(chanInfo));
            Assert.That(chan.LocalHelloData, Is.EqualTo(localHelloData));
            Assert.That(chan.Id, Is.EqualTo(chanInfo.id));
            Assert.That(chan.Name, Is.EqualTo(chanInfo.name));
            Assert.That(chan.IsTrackingMemberShip, Is.True);
            Assert.That(chan.ReportsMissingPeers, Is.True);
            Assert.That(chan.IsSyncingClocks, Is.True);
        }
    }

}