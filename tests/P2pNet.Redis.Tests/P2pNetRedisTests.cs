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
    public class P2pNetRedisTests
    {
        [Test]
        public void ConstructorWorks()
        {
            // public P2pRedis(IP2pNetClient _client, string _connectionString,  Dictionary<string, string> _config = null)
            const string dstChan = "dstChan",
                srcId = "srcId",
                msgType = "msgType",
                payload = "payload";
            const long msgId = 1234567890;

            //P2pNetMessage msg =  new P2pRedis(dstChan, srcId, msgId, msgType, payload);
            //Assert.That(msg, Is.Not.Null);
        }
    }

}