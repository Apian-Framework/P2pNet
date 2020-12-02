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

            P2pNetMessage msg = new P2pNetMessage(dstChan, srcId, msgId, msgType, payload);
            Assert.That(msg, Is.Not.Null);
            Assert.That(msg.dstChannel, Is.EqualTo(dstChan));
            Assert.That(msg.srcId, Is.EqualTo(srcId));
            Assert.That(msg.msgId, Is.EqualTo(msgId));
            Assert.That(msg.msgType, Is.EqualTo(msgType));
            Assert.That(msg. payload, Is.EqualTo(payload));
        }
    }
}