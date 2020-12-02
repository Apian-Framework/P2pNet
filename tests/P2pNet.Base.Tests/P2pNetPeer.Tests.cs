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
        [Ignore("Stop using global P2pNetBase.NowMs")]
        // Maybe find a good way to mock it? I dunno. Actually, it's jsut a static wrapper for a calculation using
        // the global DateTime.
        public void VariousTimingTests()
        {

        }
    }

}