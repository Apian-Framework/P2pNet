﻿using System.Diagnostics;
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
    public class P2pLoopbackTests
    {
        public class MockP2pNetClient : IP2pNetClient
        {
            public void OnClientMsg(string from, string to, long msSinceSent, string payload) => throw new NotImplementedException();
            public void OnPeerJoined(string channelId, string p2pId, string helloData) => throw new NotImplementedException();
            public void OnPeerMissing(string channelId, string p2pId) => throw new NotImplementedException();
            public void OnPeerReturned(string channelId, string p2pId) => throw new NotImplementedException();
            public void OnPeerLeft(string channelId, string p2pId) => throw new NotImplementedException();
            public void OnPeerSync(string channelId, string p2pId, PeerClockSyncInfo syncInfo) =>  throw new NotImplementedException();
            public void OnJoinRejected(string channelId, string reason) =>  throw new NotImplementedException();
        }

        [Test]
        public void P2pLoopback_Ctor()
        {
            // public P2pActiveMq(IP2pNetClient _client, string _connectionString,  Dictionary<string, string> _config = null)
            MockP2pNetClient cli = new MockP2pNetClient();

            P2pLoopback p2p = new P2pLoopback("hello?");
            Assert.That(p2p, Is.Not.Null);
        }
    }

}