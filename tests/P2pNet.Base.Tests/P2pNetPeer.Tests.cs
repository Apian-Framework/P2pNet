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
    public class P2pNetPeerTests
    {
        [Test]
        public void ConstructorWorks()
        {
            // public P2pNetPeer(string _p2pId, int _pingMs, int _dropMs, int _syncMs)
            const string p2pId = "p2pId";
            const int pingMs =  5010,
                dropMs = 12123,
                syncMs = 19991;

            P2pNetPeer peer = new P2pNetPeer(p2pId, pingMs, dropMs, syncMs);
            Assert.That(peer, Is.Not.Null);
            Assert.That(peer.p2pId, Is.EqualTo(p2pId));
            Assert.That(peer.helloData, Is.Null);
        }

        [Test]
        // public bool ValidateMsgId(long msgId)
        // ID must be larger that previous.
        // Side effect: if valid, previous is set to msgId
        public void ValidatMsgIdWorks()
        {
            const string p2pId = "p2pId";
            const int pingMs =  5000,
                dropMs = 10000,
                syncMs = 14000;

            P2pNetPeer peer = new P2pNetPeer(p2pId, pingMs, dropMs, syncMs);
            Assert.That(peer, Is.Not.Null);

            bool valid = peer.ValidateMsgId(1);
            Assert.That(valid, Is.True); // was 0 now 1

            valid = peer.ValidateMsgId(1);
            Assert.That(valid, Is.False); // was 1 now 1

            valid = peer.ValidateMsgId(5);
            Assert.That(valid, Is.True); // was 1 now 5

            valid = peer.ValidateMsgId(3);
            Assert.That(valid, Is.False); // was 5 now 5

        }

        // Still need to deal with:
        // public long MsSinceClockSync
        // public bool CurrentlySyncing()
        // public bool ClockNeedsSync()
        // public bool HaveTriedToContact()
        // public bool HaveHeardFrom()
        // public bool WeShouldSendHello()
        // public bool HelloTimedOut()
        // public bool HasTimedOut()
        // public bool NeedsPing()

        // using:
        // public void UpdateClockSync(long t0, long t1, long t2, long t3)
        // public void ReportSyncProgress()
        // public void UpdateLastHeardFrom()
        // public void UpdateLastSentTo()




    }

}