﻿using System.Diagnostics;
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
    public class P2pNetChannelPeerTests
    {
        const string defLocalPeerId = "defLocalPeerId",
            defChannelId = "defChannelId",
            defChannelName = "defChannelNameId",
            defLocalHelloData = "defLocalHelloData";

        const int defDropMs = 10000,
            defTrackingPingMs = 3000,
            defClockSyncOnMs = 12000,
            defMaxPeerLimired = 20;

        public P2pNetChannelInfo chInfoNoTracking() => new P2pNetChannelInfo(
            defChannelName, //
            defChannelId,
            defDropMs,
            0, // no tracking
            0, // no sync
            0 // maxPeers  <- no max
        );

        public P2pNetChannelInfo chInfoTracking() => new P2pNetChannelInfo(
            defChannelName, //
            defChannelId,
            defDropMs,
            defTrackingPingMs,
            0, // no clock sync
            0 // maxPeers  <- no max
        );

        public P2pNetChannelInfo chInfoTrackingSync() => new P2pNetChannelInfo(
            defChannelName, //
            defChannelId,
            defDropMs,
            defTrackingPingMs,
            defClockSyncOnMs,
            0 // maxPeers  <- no max
        );

        public P2pNetChannel CreateChannel(P2pNetChannelInfo info)
        {
            return new P2pNetChannel(info, defLocalHelloData);;
        }

        [Test]
        public void P2pNetChannelPeer_ConstructorWorks()
        {
            P2pNetPeer peer = new P2pNetPeer(defLocalPeerId);

            // public P2pNetChannelPeer(P2pNetPeer peer, P2pNetChannel channel)
            P2pNetChannelPeer chp = new P2pNetChannelPeer(peer, CreateChannel(chInfoTrackingSync()));
            Assert.That(chp, Is.Not.Null);
            Assert.That(chp.Peer, Is.EqualTo(peer));
            Assert.That(chp.helloData, Is.EqualTo(null));
            Assert.That(chp.P2pId, Is.EqualTo(defLocalPeerId));
            Assert.That(chp.ChannelId, Is.EqualTo(defChannelId));

            Assert.That(chp.HaveTriedToContact, Is.False);
            Assert.That(chp.HaveHeardFrom, Is.False);

            Assert.That(chp.WeShouldSendHello, Is.True); // true given the above values
            Assert.That(chp.HelloTimedOut, Is.False);
            Assert.That(chp.HasTimedOut, Is.True);
            Assert.That(chp.NeedsPing, Is.True);
            Assert.That(chp.ClockNeedsSync, Is.True);

            Assert.That(chp.ValidateMsgId(0), Is.False);
            Assert.That(chp.ValidateMsgId(1), Is.True);
            Assert.That(chp.ValidateMsgId(1), Is.False); // call updates last Id if successful
        }

        [Test]
        public void P2pNetChannelPeer_NoSync_Ctor()
        {
            P2pNetPeer peer = new P2pNetPeer(defLocalPeerId);

            // public P2pNetChannelPeer(P2pNetPeer peer, P2pNetChannel channel)
            P2pNetChannelPeer chp = new P2pNetChannelPeer(peer, CreateChannel(chInfoTracking()));
            Assert.That(chp.HasTimedOut, Is.True);
            Assert.That(chp.ClockNeedsSync, Is.False); // rest was checked above
        }

        [Test]
        public void P2pNetChannelPeer_NoTracking_Ctor()
        {
            P2pNetPeer peer = new P2pNetPeer(defLocalPeerId);

            // public P2pNetChannelPeer(P2pNetPeer peer, P2pNetChannel channel)
            P2pNetChannelPeer chp = new P2pNetChannelPeer(peer, CreateChannel(chInfoNoTracking()));

            Assert.That(chp.HasTimedOut, Is.True); // peers DO time out in untracked channels
            Assert.That(chp.WeShouldSendHello, Is.False);
            Assert.That(chp.NeedsPing, Is.False);
            Assert.That(chp.ClockNeedsSync, Is.False);
        }

    }

    [TestFixture]
    public class P2pNetChannelPeerCollectionTests
    {

    }


}