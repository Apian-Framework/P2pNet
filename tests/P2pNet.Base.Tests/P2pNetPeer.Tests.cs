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
        const int defaultPingMs =  5010,
            defaultDropMs = 12123,
            defaultSyncMs = 19991;

        public P2pNetPeer CreateDefaultTestPeer()
        {
            P2pNetPeer peer = new P2pNetPeer(defaultP2pId, defaultPingMs, defaultDropMs, defaultSyncMs);
            Assert.That(peer, Is.Not.Null);
            Assert.That(peer.p2pId, Is.EqualTo(defaultP2pId));
            Assert.That(peer.helloData, Is.Null);
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

        public void _Do_RemoteInitiatedClockSync(P2pNetPeer peer, SyncTestParams sp)
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
            // and call peer.ReportSyncProgeress()
            P2pNetDateTime.Now =() => new DateTime(t1 * TimeSpan.TicksPerMillisecond); // pretend its t1
            payload.t1 = t1; // we set this in _OnSyncMsg()
            peer.ReportSyncProgress(); // sets peer.lastClockSyncMs to -nowMs
            Assert.That(peer.CurrentlySyncing, Is.True);
            payload.t2 = t2; // local send time (set in  _send())

            // Now pretend were remote again, and have gotten the payload with t0 and t1 set
            payload.t3 = t3; // remote reciept time

            // we get the payload back...
            P2pNetDateTime.Now =() => new DateTime(t5 * TimeSpan.TicksPerMillisecond);
            peer.UpdateClockSync(payload.t2, payload.t3, t4, t5);

            Assert.That(peer.CurrentlySyncing, Is.False);

            long computedLag = ((t5 - t2) - (t4 - t3)) / 2;
            long computedTheta = ((t3 - t2) + (t4 - t5)) / 2;

            long reportedLag = prevPeerLag == 0 ? computedLag : (prevPeerLag + computedLag)/2;
            long reportedOffset = prevPeerOffset == 0 ? computedTheta : (prevPeerOffset+computedTheta)/2;

            Assert.That(peer.ClockOffsetMs, Is.EqualTo(reportedOffset));;
            Assert.That(peer.NetworkLagMs, Is.EqualTo(reportedLag));
        }


        public void _Do_LocalInitiatedClockSync(P2pNetPeer peer, SyncTestParams sp)
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
             //    calls peer.ReportSyncProgress()
             //    sends an empty sync paylod
            P2pNetDateTime.Now =() => new DateTime(t0 * TimeSpan.TicksPerMillisecond); // pretend its t0
            peer.ReportSyncProgress(); // sets peer.lastClockSyncMs to -nowMs

            // Step 2: On the remote peer payload t0 and t1 set set and sent back
            payload.t0 = t0;
            payload.t1 = t1;

            // Step 3: meanwhile, back at the farm...
            P2pNetDateTime.Now =() => new DateTime(t3 * TimeSpan.TicksPerMillisecond);
            payload.t2 = t2;
            payload.t3 = t3;

            // In real life we'd send the payload back, but we already have all the info we nee locally
            peer.UpdateClockSync(payload.t0, payload.t1, payload.t2, payload.t3);

            Assert.That(peer.CurrentlySyncing, Is.False);

            long computedLag = ((t3 - t0) - (t2 - t1)) / 2;
            long computedTheta = ((t1 - t0) + (t2 - t3)) / 2;

            long reportedLag = prevPeerLag == 0 ? computedLag : (prevPeerLag + computedLag)/2;
            long reportedOffset = prevPeerOffset == 0 ? computedTheta : (prevPeerOffset+computedTheta)/2;

            Assert.That(peer.ClockOffsetMs, Is.EqualTo(reportedOffset));
            Assert.That(peer.NetworkLagMs, Is.EqualTo(reportedLag));
        }

        [Test]
        public void P2pNetPeer_Ctor()
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

        // [Test]
        // // Trivial coverage tests
        // public void P2pNetPeer_PublicPropertyOneLiners()
        // {
        //     P2pNetPeer peer = CreateDefaultTestPeer();
        //     Assert.That(peer, Is.Not.Null);

        //    long lag = peer.NetworkLagMs;
        //    Assert.That(lag, Is.EqualTo(0));

        //     long clockOffMs = peer.ClockOffsetMs;
        //     Assert.That(clockOffMs, Is.EqualTo(0));

        //     long sinceSync = peer.MsSinceClockSync;
        //     Assert.That(sinceSync, Is.EqualTo(-1));
        // }


        [Test]
        // public bool ValidateMsgId(long msgId)
        // ID must be larger that previous.
        // Side effect: if valid, previous is set to msgId
        public void P2pNetPeer_ValidatMsgId()
        {
            P2pNetPeer peer = CreateDefaultTestPeer();
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

        [Test]
        // Exercise the peer clock sync internals
        public void P2pNetPeer_RemoteInitiatedClockSync()
        {
            P2pNetPeer peer = CreateDefaultTestPeer();
            Assert.That(peer, Is.Not.Null);
            Assert.That(peer.ClockNeedsSync(), Is.True);
            Assert.That(peer.CurrentlySyncing, Is.False);

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

            bool bob = peer.ClockNeedsSync();

            Assert.That(peer.ClockNeedsSync(), Is.False);

        }

        [Test]
        public void P2pNetPeer_LocalInitiatedClockSync()
        {
            P2pNetPeer peer = CreateDefaultTestPeer();
            Assert.That(peer, Is.Not.Null);
            Assert.That(peer.ClockNeedsSync(), Is.True);
            Assert.That(peer.CurrentlySyncing, Is.False);

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
        public void P2pNetPeer_ClockSyncBothWays()
        {
            // Different branches happen if a clock has been synced more than once
            P2pNetPeer peer = CreateDefaultTestPeer();
            Assert.That(peer, Is.Not.Null);
            Assert.That(peer.ClockNeedsSync(), Is.True);
            Assert.That(peer.CurrentlySyncing, Is.False);
            Assert.That(peer.MsSinceClockSync, Is.EqualTo(-1));

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

            Assert.That(peer.ClockNeedsSync(), Is.False);
            Assert.That(peer.MsSinceClockSync, Is.EqualTo(0)); // local "clock" hasn't moved

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
            Assert.That(peer.ClockNeedsSync(), Is.True);
            Assert.That(peer.CurrentlySyncing, Is.False);

            _Do_LocalInitiatedClockSync(peer, testParams2);

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