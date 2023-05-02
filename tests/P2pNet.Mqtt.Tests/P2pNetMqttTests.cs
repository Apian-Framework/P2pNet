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
    public class P2pNetMqttTests
    {
        public class MockP2pNetClient : IP2pNetClient
        {
            public void OnClientMsg(string from, string to, long msSinceSent, string payload)
            {
                throw new NotImplementedException();
            }

            public void OnPeerJoined(string chanId, string p2pId, string helloData)
            {
                throw new NotImplementedException();
            }

            public void OnPeerMissing(string channelId, string p2pId)
            {
                throw new NotImplementedException();
            }

            public void OnPeerReturned(string channelId, string p2pId)
            {
                throw new NotImplementedException();
            }

            public void OnPeerLeft(string chanId, string p2pId)
            {
                throw new NotImplementedException();
            }

            public void OnPeerSync(string channelId, string p2pId, PeerClockSyncInfo syncInfo)
            {
                throw new NotImplementedException();
            }

            public string P2pHelloData()
            {
                throw new NotImplementedException();
            }

            public void OnJoinRejected(string channelId, string reason) =>  throw new NotImplementedException();
        }

        [Test]
        [Ignore("Need to figure out Mqtt mocking.")]
        public void P2pMqtt_Ctor()
        {
            P2pMqtt p2p = new P2pMqtt("hello?");
            Assert.That(p2p, Is.Not.Null );
        }
    }

}