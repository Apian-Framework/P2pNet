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
    //
    // These are trivial tests for classes which are pretty much just data stores
    //
    [TestFixture]
    public class PeerClockSyncInfoTests
    {
        // Only has a constructor test
        [Test]
        public void ClockSyncData_ConstructorWorks()
        {
            // public PeerClockSyncInfo(string pid, long since, long offset, long lag)
            const string pid = "pid";
            const int count = 12,
                since = 123456,
                offfset = 3245,
                lag = 250;
            const double lagSigma = 1.2, offsetSigma = 4.3;

            PeerClockSyncInfo syncData = new PeerClockSyncInfo(pid, count, since, offfset, offsetSigma, lag, lagSigma);
            Assert.That(syncData, Is.Not.Null);
            Assert.That(syncData.peerId, Is.EqualTo(pid));
            Assert.That(syncData.msSinceLastSync, Is.EqualTo(since));
            Assert.That(syncData.sysClockOffsetMs, Is.EqualTo(offfset));
            Assert.That(syncData.networkLagMs, Is.EqualTo(lag));
        }
    }

    [TestFixture]
    public class P2pNetMessageTests
    {
        [Test]
        public void ConstructorWorks()
        {
            // public P2pNetMessage(string _dstChan, string _srcId, long _msgId, string _msgType, string _payload)
            const string dstChan = "dstChan",
                srcId = "srcId",
                msgType = "msgType",
                payload = "payload";
            const long msgId = 1234567890;

            P2pNetMessage msg =  new P2pNetMessage(dstChan, srcId, msgId, msgType, payload);
            Assert.That(msg, Is.Not.Null);
            Assert.That(msg.dstChannel, Is.EqualTo(dstChan));
            Assert.That(msg.srcId, Is.EqualTo(srcId));
            Assert.That(msg.msgId, Is.EqualTo(msgId));
            Assert.That(msg.msgType, Is.EqualTo(msgType));
            Assert.That(msg. payload, Is.EqualTo(payload));
        }
    }

    [TestFixture]
    public class SyncPayloadTests
    {
        [Test]
        public void SyncPayload_Ctor()
        {
            // public SyncPayload() {t0=0; t1=0; t2=0; t3=0;} (all longs)

            SyncPayload pld =  new SyncPayload();
            Assert.That(pld, Is.Not.Null);
            Assert.That(pld.t0, Is.EqualTo(0));
            Assert.That(pld.t1, Is.EqualTo(0));
            Assert.That(pld.t2, Is.EqualTo(0));
            Assert.That(pld.t3, Is.EqualTo(0));
        }

        [Test]
        public void SyncPayload_ToString()
        {
            string testStringRep = "{t0:100 t1:100 t2:100 t3:100}";
            // public SyncPayload() {t0=0; t1=0; t2=0; t3=0;} (all longs)
            SyncPayload pld =  new SyncPayload();
            Assert.That(pld, Is.Not.Null);
            pld.t0 = 100;
            pld.t1 = 100;
            pld.t2 = 100;
            pld.t3 = 100;

            string str = pld.ToString();
            Assert.That(str, Is.EqualTo(testStringRep));
        }
    }

    [TestFixture]
    public class HelloPayloadTests
    {
        [Test]
        public void HelloPayload_Ctor()
        {
            // public HelloPayload(P2pNetChannelInfo info, string helloData) {channelInfo = info; channelHelloData = helloData;}
            P2pNetChannelInfo channelInfo = new P2pNetChannelInfo(
                "chanName",
                "chanId",
                0, // dropMs
                0, // pingMs
                0, // missingMs
                0, // netSyncMs
                0  // maxPeers
            );

            const string helloData = "helloData";
            const string peerAddr = "12345678";


            HelloPayload pld =  new HelloPayload(peerAddr, channelInfo, helloData);
            Assert.That(pld, Is.Not.Null);
            Assert.That(pld.peerAddress, Is.EqualTo(peerAddr));
            Assert.That(pld.channelInfo, Is.EqualTo(channelInfo));
            Assert.That(pld.peerChannelHelloData, Is.EqualTo(helloData));

        }

    }

}