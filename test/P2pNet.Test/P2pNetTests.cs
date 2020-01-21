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
    public class TestClient : IP2pNetClient
    {
        public class PeerData
        {
            public string town;
            public string name;
            public PeerData(string _name, string _town)
            {
                town = _town;
                name = _name;
            }
        }

        public class MsgRecord
        {
            public string from;
            public string to;
            public string msgData;

            public MsgRecord(string _from, string _to, string _msgData)
            {
                from = _from;
                to = _to;
                msgData = _msgData;
            }
        }
        public IP2pNet p2p;
        public string name;
        public string town;
        public string p2pId;
        public Dictionary<string, PeerData> localPeers;
        public List<MsgRecord> msgList;


        public TestClient(string _name, string _town)
        {
            town = _town;
            name = _name;
            localPeers = new Dictionary<string, PeerData>();
            msgList = new List<MsgRecord>();
        }

        public bool Connect(string connectionStr)
        {
            p2p = null;
            string[] parts = connectionStr.Split(new string[]{"::"},StringSplitOptions.None); // Yikes! This is fugly.
            switch(parts[0].ToLower())
            {
                case "p2predis":
                    p2p = new P2pRedis(this, parts[1]);
                    break;
                case "p2ploopback":
                    p2p = new P2pLoopback(this, null);
                    break;         
                case "p2pactivemq":
                    p2p = new P2pActiveMq(this, parts[1]);
                    break;                               
                default:
                    throw( new Exception($"Invalid connection type: {parts[0]}"));
            }
            if (p2p == null)
                throw( new Exception("p2p Connect failed"));
            return p2p != null;
        }

        public string Join(string gameChannel)
        {   
            p2pId = p2p.GetId();
            p2p.Join(gameChannel);
            return p2pId;
        }

        public string P2pHelloData()
        {
            return JsonConvert.SerializeObject(new PeerData(name, town));
        }
        public void OnPeerJoined(string p2pId, string helloData)
        {
            localPeers[p2pId] = JsonConvert.DeserializeObject<PeerData>(helloData);
            TestContext.Out.WriteLine(string.Format("{0} joined. Name: {1}", p2pId, localPeers[p2pId].name));            
        }
        public void OnPeerLeft(string p2pId)
        {
            localPeers.Remove(p2pId);
        }
        public void OnClientMsg(string from, string to, string msgData)
        {
            msgList.Add( new MsgRecord(from, to, msgData));
        }
    }

    // Utils
    static class TestGuts
    {
        public static void _ClientShouldConnect(string connectionString)
        {
            //same as above, relly
            TestClient tc = new TestClient("jim","meredith");
            tc.Connect(connectionString);
            Assert.That(tc.p2p, Is.Not.Null);
            Assert.That(tc.p2p.GetId(), Is.Not.Null);
        }

        public static async Task _ClientShouldJoin(string connectionString)
        {
            string testGameChannel = System.Guid.NewGuid().ToString(); // if tests run in parallel they need separate channels
            //same as above, really
            TestClient tc = new TestClient("jim","meredith");
            tc.Connect(connectionString);
            tc.Join(testGameChannel);
            Assert.That(tc.p2p.GetId(), Is.EqualTo(tc.p2pId));
            tc.p2p.Send(testGameChannel, "Hello game channel");  
            await Task.Delay(250); // &&& SUPER LAME!!!!
            tc.p2p.Loop();
            Assert.That(tc.p2p.GetPeerIds().Count, Is.EqualTo(0)); // should be zero. We're not in our own list
            Assert.That(tc.msgList.Count, Is.EqualTo(1));
        }   

        public static async Task _TwoClientsShouldTalk(string connectionStr)
        {
            string testGameChannel = System.Guid.NewGuid().ToString();

            TestClient tcJim = new TestClient("jim","meredith");
            tcJim.Connect(connectionStr);

            await Task.Delay(250);
            tcJim.p2p.Loop();            
            TestClient tcEllen = new TestClient("ellen","raymond");
            tcEllen.Connect(connectionStr);

            await Task.Delay(100);
            tcJim.p2p.Loop();            
            tcEllen.p2p.Loop();              
            Assert.That(tcJim.localPeers.Values.Count, Is.EqualTo(0));            
            tcJim.Join(testGameChannel);

            await Task.Delay(300);
            tcJim.p2p.Loop();            
            tcEllen.p2p.Loop();        
            tcEllen.Join(testGameChannel);

            await Task.Delay(250); // THe join hello/response handshake takes 2 loops
            tcJim.p2p.Loop();            
            tcEllen.p2p.Loop();  

            await Task.Delay(250); // THe join hello/response handshake takes 2 loops
            tcJim.p2p.Loop();            
            tcEllen.p2p.Loop();  

            await Task.Delay(250);
            tcJim.p2p.Loop();            
            tcEllen.p2p.Loop();            
            //Assert.That(tcJim.localPeers[tcEllen.p2pId], Is.EqualTo(tcEllen.localPeers[tcEllen.p2pId]));
            Assert.That(tcJim.localPeers.Keys.Count, Is.EqualTo(1));
            Assert.That(tcEllen.localPeers.Keys.Count, Is.EqualTo(1));            
            Assert.That(tcEllen.msgList.Count, Is.EqualTo(0));
            Assert.That(tcJim.msgList.Count, Is.EqualTo(0));
            tcJim.p2p.Send(testGameChannel, "Hello game channel");
            tcJim.p2p.Send(tcEllen.p2pId, "Hello Ellen");

            await Task.Delay(200);
            tcJim.p2p.Loop();            
            tcEllen.p2p.Loop();              
            Assert.That(tcEllen.msgList.Count, Is.EqualTo(2));
            Assert.That(tcJim.msgList.Count, Is.EqualTo(1));
        }             
    }    

    [TestFixture]
    public class P2pLoopbackTests
    {

        public const string rawLoopbackConnectionStr = "";
        public const string clientLoopbackConnectionStr = "p2ploopback::";        

        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void ShouldCreateP2pRedis()
        {
            TestClient tc = new TestClient("jim","meredith");
            IP2pNet p2p = new P2pLoopback(tc, rawLoopbackConnectionStr);
            Assert.That(p2p, Is.Not.Null);
            Assert.That(p2p.GetId(), Is.Not.Null);
        }
        [Test]
        public void P2pShouldConnect()
        {
            TestClient tc = new TestClient("jim","meredith");
            IP2pNet p2p = new P2pLoopback(tc, rawLoopbackConnectionStr);
            Assert.That(p2p, Is.Not.Null);
            Assert.That(p2p.GetId(), Is.Not.Null);
        }
        [Test]
        public void ClientShouldConnect()
        {
            TestGuts._ClientShouldConnect(clientLoopbackConnectionStr);
        }

        [Test]
        public async Task ClientShouldJoin()
        {
            await TestGuts._ClientShouldJoin(clientLoopbackConnectionStr);            
        }

        // 2 clients CAN'T talk using the loopback impl
    }

    [TestFixture]
    public class P2pRedisTests
    {
        public const string rawRedisConnectionStr = "192.168.1.195,password=sparky-redis79";
        public const string clientRedisConnectionStr = "p2predis::" + rawRedisConnectionStr;        

        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void ShouldCreateP2pRedis()
        {
            TestClient tc = new TestClient("jim","meredith");
            IP2pNet p2p = new P2pRedis(tc, rawRedisConnectionStr);
            Assert.That(p2p, Is.Not.Null);
            Assert.That(p2p.GetId(), Is.Not.Null);
        }
        [Test]
        public void P2pShouldConnect()
        {
            TestClient tc = new TestClient("jim","meredith");
            IP2pNet p2p = new P2pRedis(tc, rawRedisConnectionStr);
            Assert.That(p2p, Is.Not.Null);
            Assert.That(p2p.GetId(), Is.Not.Null);
        }
        [Test]
        public void ClientShouldConnect()
        {
            TestGuts._ClientShouldConnect(clientRedisConnectionStr);
        }

        [Test]
        public async Task ClientShouldJoin()
        {
            await TestGuts._ClientShouldJoin(clientRedisConnectionStr);            
        }

        [Test]
        public async Task TwoClientsShouldTalk()
        {           
            await TestGuts._TwoClientsShouldTalk(clientRedisConnectionStr);
        }
    }

    [TestFixture]
    public class P2pActiveMqTests
    {      
        public const string rawActiveMqConnectionStr = "beam,beamuserperson,activemq:tcp://sparkyx:61616"; 
        public const string clientActiveMqConnectionStr = "p2pactivemq::beam,beamuserperson,activemq:tcp://sparkyx:61616"; 
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void ShouldCreateP2pActiveMq()
        {
            TestClient tc = new TestClient("jim","meredith");
            IP2pNet p2p = new P2pActiveMq(tc, rawActiveMqConnectionStr);
            Assert.That(p2p, Is.Not.Null);
            Assert.That(p2p.GetId(), Is.Not.Null);
        }
        [Test]
        public void P2pShouldConnect()
        {
            TestClient tc = new TestClient("jim","meredith");
            IP2pNet p2p = new P2pActiveMq(tc, rawActiveMqConnectionStr);
            Assert.That(p2p, Is.Not.Null);
            Assert.That(p2p.GetId(), Is.Not.Null);
        }
        [Test]
        public void ClientShouldConnect()
        {
            TestGuts._ClientShouldConnect(clientActiveMqConnectionStr);
        }

        [Test]
        public async Task ClientShouldJoin()
        {
            await TestGuts._ClientShouldJoin(clientActiveMqConnectionStr);            
        }

        [Test]
        public async Task TwoClientsShouldTalk()
        {
            UniLogger.GetLogger("P2pNet").LogLevel = UniLogger.Level.Debug;
            await TestGuts._TwoClientsShouldTalk(clientActiveMqConnectionStr);
        }        

    }

}
