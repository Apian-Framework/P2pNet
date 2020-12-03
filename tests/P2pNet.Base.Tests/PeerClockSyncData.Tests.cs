using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Newtonsoft.Json;
using P2pNet;
using UniLog;

namespace P2pNetTests
{
    [TestFixture]
    public class PeerClockSynvDataTests
    {
        // Only has a constructor test
        [Test]
        public void ConstructorWorks()
        {
            // public PeerClockSyncData(string pid, long since, long offset, long lag)
            const string pid = "pid";
            const long since = 123456,
                offfset = 3245,
                lag = 250;

            PeerClockSyncData syncData = new PeerClockSyncData(pid, since, offfset, lag);
            Assert.That(syncData, Is.Not.Null);
            Assert.That(syncData.peerId, Is.EqualTo(pid));
            Assert.That(syncData.msSinceLastSync, Is.EqualTo(since));
            Assert.That(syncData.clockOffsetMs, Is.EqualTo(offfset));
            Assert.That(syncData.networkLagMs, Is.EqualTo(lag));
        }
    }

}