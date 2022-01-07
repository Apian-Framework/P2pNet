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
    public class P2pNetPeerTests
    {
        const string defaultP2pId = "peerP2pId";
        const int  defaultSyncMs = 19991;

        public class TestPeer : P2pNetPeer
        {
            public TestPeer( string p2pId) : base(p2pId) {}

            public long NetworkLagMs => clockSync.NetworkLagMs;
            public long ClockOffsetMs => clockSync.ClockOffsetMs;
            public long LatestSyncActivityMs => clockSync.lastActivityMs;

        }


        public TestPeer CreateDefaultTestPeer()
        {
            TestPeer peer = new TestPeer(defaultP2pId);
            Assert.That(peer, Is.Not.Null);
            Assert.That(peer.p2pId, Is.EqualTo(defaultP2pId));
            return peer;
        }

        public struct SyncTestParams
        {
          public long localBaseMs;
          public long remoteOffset; // global time difference between remote and local clocks
          public long msg0Lag;  // net lag for 1st msg (from originator)
          public long lat1;     // recipient latency before sending repl1
          public long msg1Lag;  // reply1 net lag (rcp->org)
          public long lat2;     // latency before sending reply2 (org->cp)
          public long msg2Lag;  // reply2 net lag (org->rcp)
        }

        public void _Do_RemoteInitiatedClockSync(TestPeer peer, SyncTestParams sp)
        {
            long prevPeerLag = peer.NetworkLagMs;  // used at bottom
            long prevPeerOffset = peer.ClockOffsetMs;

            // Set up times
            long t0 = sp.localBaseMs + sp.remoteOffset;// Initial send time (originator)

            long t1 = sp.localBaseMs  + sp.msg0Lag; // Initial receipt  (recipient)
            long t2 = t1 + sp.lat1; // reply1 send (recipient) includes local latency

            long t3 = (t2 + sp.remoteOffset) + sp.msg1Lag; // reply1 receipt (org)
            long t4 = t3 + sp.lat2; // reply 2 send time (org)

            long t5 = (t4 - sp.remoteOffset) + sp.msg2Lag ; // reply2 receipt (recipient)

            SyncPayload payload = new SyncPayload();

             // step 1: we are the remote peer. Set t0 and send...
            payload.t0 = t0; // was set when we got it...

            // step 2:
            // Act as if we have received an initial msg with just t0 set..
            // in _OnSyncMsg we would set t1 when we receoved it (our clock)
            // and call peer.ReportInterimSyncProgeress()
            P2pNetDateTime.Now =() => new DateTime(t1 * TimeSpan.TicksPerMillisecond); // pretend its t1
            payload.t1 = t1; // we set this in _OnSyncMsg()
            peer.ReportInterimSyncProgress(); // sets peer.lastClockSyncMs to -nowMs

            payload.t2 = t2; // local send time (set in  _send())

            // Now pretend were remote again, and have gotten the payload with t0 and t1 set
            payload.t3 = t3; // remote reciept time

            // we get the payload back...
            P2pNetDateTime.Now =() => new DateTime(t5 * TimeSpan.TicksPerMillisecond);
            peer.CompleteClockSync(payload.t2, payload.t3, t4, t5);

            long computedLag = ((t5 - t2) - (t4 - t3)) / 2;
            long computedTheta = ((t3 - t2) + (t4 - t5)) / 2;

            long reportedLag = prevPeerLag == 0 ? computedLag : (prevPeerLag + computedLag)/2;
            long reportedOffset = prevPeerOffset == 0 ? computedTheta : (prevPeerOffset+computedTheta)/2;

            Assert.That(peer.ClockOffsetMs, Is.EqualTo(reportedOffset));;
            Assert.That(peer.NetworkLagMs, Is.EqualTo(reportedLag));
        }


        public void _Do_LocalInitiatedClockSync(TestPeer peer, SyncTestParams sp)
        {
            long prevPeerLag = peer.NetworkLagMs;  // used at bottom
            long prevPeerOffset = peer.ClockOffsetMs;

            // Set up times
            long t0 = sp.localBaseMs;// Initial send time (originator)

            long t1 = (sp.localBaseMs  + sp.remoteOffset)  + sp.msg0Lag; // Initial receipt  (recipient)
            long t2 = t1 + sp.lat1; // reply1 send (recipient) includes local latency

            long t3 = (t2 - sp.remoteOffset) + sp.msg1Lag; // reply1 receipt (org)
            long t4 = t3 + sp.lat2; // reply 2 send time (org)

            long t5 = (t4 + sp.remoteOffset) + sp.msg2Lag ; // reply2 receipt (recipient)

            SyncPayload payload = new SyncPayload();

             // step 1: We are initiating.
             // _SendSync()
             //    calls peer.ReportInterimSyncProgress()
             //    sends an empty sync paylod
            P2pNetDateTime.Now =() => new DateTime(t0 * TimeSpan.TicksPerMillisecond); // pretend its t0
            peer.ReportInterimSyncProgress(); // sets peer.lastClockSyncMs to -nowMs

            // Step 2: On the remote peer payload t0 and t1 set set and sent back
            payload.t0 = t0;
            payload.t1 = t1;

            // Step 3: meanwhile, back at the farm...
            P2pNetDateTime.Now =() => new DateTime(t3 * TimeSpan.TicksPerMillisecond);
            payload.t2 = t2;
            payload.t3 = t3;

            // In real life we'd send the payload back, but we already have all the info we need locally
            peer.CompleteClockSync(payload.t0, payload.t1, payload.t2, payload.t3);

            long computedLag = ((t3 - t0) - (t2 - t1)) / 2;
            long computedTheta = ((t1 - t0) + (t2 - t3)) / 2;

            long reportedLag, reportedOffset;
            float lagVar, offsetVar;

            // This is not at all how it works anymore. SHouldn't be doing these
            (reportedLag, lagVar) =  PeerClockSyncCalc.TraditionalEwma(computedLag, prevPeerLag, 0, 1, 8);
            (reportedOffset, offsetVar) = PeerClockSyncCalc.TraditionalEwma(computedTheta, prevPeerOffset, 0, 1, 8);

            // FIXME: really need to check these once stats get settled - do the above some better way
           // Assert.That(peer.ClockOffsetMs, Is.EqualTo(reportedOffset));
           // Assert.That(peer.NetworkLagMs, Is.EqualTo(reportedLag));
        }

        [Test]
        public void P2pNetPeer_Ctor()
        {
            // public P2pNetPeer(string _p2pId, int _pingMs, int _dropMs, int _syncMs)
            const string p2pId = "p2pId";

            P2pNetPeer peer = new P2pNetPeer(p2pId);
            Assert.That(peer, Is.Not.Null);
            Assert.That(peer.p2pId, Is.EqualTo(p2pId));
        }


        [Test]
        // Exercise the peer clock sync internals
        [Ignore("ClockSync has completely changed. Rewrite the tests.")]
        public void P2pNetPeer_RemoteInitiatedClockSync()
        {
            TestPeer peer = CreateDefaultTestPeer();
            Assert.That(peer, Is.Not.Null);
            Assert.That(peer.ClockNeedsSync(defaultSyncMs), Is.True);
            Assert.That(peer.LatestSyncActivityMs, Is.EqualTo(0));

            SyncTestParams testParams = new SyncTestParams()
            {
                localBaseMs = 63743025676711,
                remoteOffset = 25,
                msg0Lag = 22,
                lat1  = 5,
                msg1Lag = 32,
                lat2 = 7,
                msg2Lag = 24
            };

            _Do_RemoteInitiatedClockSync(peer, testParams);

            bool bob = peer.ClockNeedsSync(defaultSyncMs);

            Assert.That(peer.ClockNeedsSync(defaultSyncMs), Is.False);

        }

        [Test]
        [Ignore("ClockSync has completely changed. Rewrite the tests.")]
        public void P2pNetPeer_LocalInitiatedClockSync()
        {
            TestPeer peer = CreateDefaultTestPeer();
            Assert.That(peer, Is.Not.Null);
            Assert.That(peer.ClockNeedsSync(defaultSyncMs), Is.True);

            SyncTestParams testParams = new SyncTestParams()
            {
                localBaseMs = 63743025676711,
                remoteOffset = 25,
                msg0Lag = 22,
                lat1  = 5,
                msg1Lag = 32,
                lat2 = 7,
                msg2Lag = 24
            };

            _Do_LocalInitiatedClockSync(peer, testParams);
        }

        [Test]
        [Ignore("ClockSync has completely changed. Rewrite the tests.")]
        public void P2pNetPeer_ClockSyncBothWays()
        {
            // Different branches happen if a clock has been synced more than once
            TestPeer peer = CreateDefaultTestPeer();
            Assert.That(peer, Is.Not.Null);
            Assert.That(peer.ClockNeedsSync(defaultSyncMs), Is.True);
            Assert.That(peer.LatestSyncActivityMs, Is.EqualTo(0));

            SyncTestParams testParams = new SyncTestParams()
            {
                localBaseMs = 63743025676711,
                remoteOffset = 25,
                msg0Lag = 22,
                lat1  = 5,
                msg1Lag = 32,
                lat2 = 7,
                msg2Lag = 24
            };

            _Do_RemoteInitiatedClockSync(peer, testParams);

            Assert.That(peer.ClockNeedsSync(defaultSyncMs), Is.False);
            Assert.That(peer.ClockSyncInfo.msSinceLastSync, Is.EqualTo(0)); // local "clock" hasn't moved

            SyncTestParams testParams2 = new SyncTestParams()
            {
                localBaseMs = testParams.localBaseMs + defaultSyncMs + 500, // need it again
                remoteOffset = 25,
                msg0Lag = 22,
                lat1  = 5,
                msg1Lag = 32,
                lat2 = 7,
                msg2Lag = 24
            };

            P2pNetDateTime.Now =() => new DateTime(testParams2.localBaseMs * TimeSpan.TicksPerMillisecond);
            Assert.That(peer.ClockNeedsSync(defaultSyncMs), Is.True);

            _Do_LocalInitiatedClockSync(peer, testParams2);

        }

        [Test]
        public void P2pNetPeer_LasstHeardFromAndSentTo()
        {
            long testMs = 63743025676711; // some time during Dec 8, 2020
            DateTime testDT = new DateTime(testMs *  TimeSpan.TicksPerMillisecond);

            TestPeer peer = CreateDefaultTestPeer();
            Assert.That(peer.LastHeardFromTs, Is.EqualTo(0));
            Assert.That(peer.LastSentToTs, Is.EqualTo(0));

            P2pNetDateTime.Now =() => new DateTime(testDT.Ticks);

            peer.UpdateLastHeardFrom();
            Assert.That(peer.LastHeardFromTs, Is.EqualTo(testMs));

            peer.UpdateLastSentTo();
            Assert.That(peer.LastSentToTs, Is.EqualTo(testMs));

        }




    }

}