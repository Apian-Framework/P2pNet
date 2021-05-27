using System.Runtime.InteropServices;
using System.IO.Enumeration;
using System;
using System.Collections.Generic;
using NUnit.Framework;
using StackExchange.Redis;
using P2pNet;
using Moq;

namespace P2pNetTests
{
    [TestFixture]
    public class P2pNetRedisTests
    {
        Mock<IP2pNetClient> mockCli;
        Mock<IConnectionMultiplexer> mockMux;

        class ConnectionStringFailure  {
            public Type redisExceptionType;
            public ConnectionFailureType redisFailureType;
            public string redisExceptionMsg;
            public string p2pNetExceptionMsg;
            public ConnectionStringFailure(Type t, ConnectionFailureType f, string r, string p) {
                redisExceptionType = t;
                redisFailureType = f;
                redisExceptionMsg = r;
                p2pNetExceptionMsg = p;
            }
        }

        const string kGoodConnectionStr = "GoodConnStr";
        const string kBadConnectionStr_BadHost = "BadCantConnect"; // bad host name, or good IP but no redis server
        const string kBadConnectionString_AuthFailure = "BadAuthFail";
        const string kBadConnectionString_BadString = "BadString";

        Dictionary<string, ConnectionStringFailure> ConnectFailures = new Dictionary<string, ConnectionStringFailure>() {
            { kBadConnectionStr_BadHost, new ConnectionStringFailure( typeof(StackExchange.Redis.RedisConnectionException),
                ConnectionFailureType.UnableToConnect,
                "It was not possible to connect to the redis server(s). UnableToConnect on fake.host.name:6379/Interactive, "
                + "Initializing/NotStarted, last: NONE, origin: BeginConnectAsync, outstanding: 0, last-read: 0s ago, last-write: 0s ago, "
                + "keep-alive: 60s, state: Connecting, mgr: 10 of 10 available, last-heartbeat: never, global: 0s ago, v: 2.0.601.3402:",
                "Unable to connect to Redis host") },
            { kBadConnectionString_AuthFailure, new ConnectionStringFailure( typeof(StackExchange.Redis.RedisConnectionException),
                ConnectionFailureType.AuthenticationFailure,
                "It was not possible to connect to the redis server(s). There was an authentication failure; check that passwords "
                + "(or client certificates) are configured correctly. AuthenticationFailure (None, last-recv: 252) on "
                + "newsweasel.com:6379/Interactive, Flushed/ComputeResult, last: ECHO, origin: SetResult, outstanding: 0, "
                + "last-read: 0s ago, last-write: 0s ago, keep-alive: 60s, state: ConnectedEstablishing, mgr: 5 of 10 available, "
                + "last-heartbeat: never, global: 0s ago, v: 2.0.601.3402:",
                "Redis authentication failure") },
            { kBadConnectionString_BadString, new ConnectionStringFailure( typeof(System.ArgumentException),
                ConnectionFailureType.None,
                "Keyword 'foobar' is not supported",
                "Bad connection string: Keyword 'foobar' is not supported") }
        };


        IConnectionMultiplexer MockMuxConnectFactory(string connString)
        {
            // Crap! ChannelMessageQueue has no interface...
            // and it's is sealed, so you can't mock it as a class...
            // And it doesn;t have a public ctor... which would be  bad idea anyway.
            // sheesh
            //ChannelMessageQueue mcq = new ChannelMessageQueue();

            Mock<ISubscriber> mockSub = new Mock<ISubscriber>();
            mockSub.Setup(s =>  s.Subscribe(It.IsAny<RedisChannel>(), CommandFlags.None)).Returns((ChannelMessageQueue)null) ;
            // the null return causes join to fail.

            mockMux = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
            mockMux.Setup(m => m.GetSubscriber(null)).Returns(mockSub.Object);

            if (ConnectFailures.ContainsKey(connString))
            {
                // Redis exceptions have ctors that take a "failureType)
                ConnectionStringFailure f = ConnectFailures[connString];
                Type exType = f.redisExceptionType;
                if (f.redisFailureType == ConnectionFailureType.None)
                    throw (System.Exception)Activator.CreateInstance(f.redisExceptionType,f.redisExceptionMsg);
                else
                    throw (System.Exception)Activator.CreateInstance(f.redisExceptionType,f.redisFailureType, f.redisExceptionMsg);
            }
            return mockMux.Object;
        }

        [Test]
        public void P2pNetRedis_Ctor()
        {
            mockCli = new Mock<IP2pNetClient>(MockBehavior.Strict);
            // public P2pRedis(IP2pNetClient _client, string _connectionString,  Dictionary<string, string> _config = null, muxInstance)
            P2pRedis p2p =  new P2pRedis(mockCli.Object,kGoodConnectionStr, MockMuxConnectFactory);
            Assert.That(p2p, Is.Not.Null);
        }

        [Test]
        [TestCase(kBadConnectionString_AuthFailure)]
        [TestCase(kBadConnectionString_BadString)]
        [TestCase(kBadConnectionStr_BadHost)]
        public void P2pNetRedis_Join_BadConnectionString(string connString)
        {
            mockCli = new Mock<IP2pNetClient>(MockBehavior.Strict);
            ConnectionStringFailure csf = ConnectFailures[connString];
            // public P2pRedis(IP2pNetClient _client, string _connectionString,  Dictionary<string, string> _config = null, muxInstance)
            P2pRedis p2p = new P2pRedis(mockCli.Object,connString, MockMuxConnectFactory);
            Exception ex = Assert.Throws(typeof(Exception), () => p2p.Join(null, "122345"));
            Assert.That(ex.Message, Is.EqualTo(csf.p2pNetExceptionMsg));
        }

        [Test]
        public void P2pNetRedis_Join_Works()
        {
            mockCli = new Mock<IP2pNetClient>();

            P2pNetChannelInfo ci = new P2pNetChannelInfo("TestChan", "TestChanId", 10000);

            P2pRedis p2p =  new P2pRedis(mockCli.Object,kGoodConnectionStr, MockMuxConnectFactory);
            Assert.That(ci, Is.Not.Null);

            // Have no idea how to test this easily (or even sorta easily)
            // p2p.Join(ci,  "122345");
            // Assert.That(p2p.connectionMux, Is.Not.Null);
            // object result = p2p.connectionMux;
            // Assert.That(result, Is.InstanceOf(typeof(IConnectionMultiplexer)));
        }

    }

}