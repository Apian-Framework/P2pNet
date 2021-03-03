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
    //
    // These are trivial tests for classes which are pretty much just data stores
    //
    [TestFixture]
    public class P2pNetDateTimeTests
    {
        // This class is a test-friendly version of DateTime.
        [Test]
        public void P2pNetDateTime_Default()
        {
            P2pNetDateTime.Now = () => DateTime.Now; // reset to default
            long directMs0 = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            long t0 = P2pNetDateTime.NowMs;
            long directMs1 = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

            Assert.That(directMs0, Is.LessThanOrEqualTo(t0));
            Assert.That(directMs1, Is.GreaterThanOrEqualTo(t0));

        }

        [Test]
        public void P2pNetDateTime_Custom()
        {
            long testMs = 63743025676711; // some time during Dec 8, 2020
            DateTime testDT = new DateTime(testMs *  TimeSpan.TicksPerMillisecond);

            long origMs = P2pNetDateTime.NowMs; // kinda dopey, but need it for test coverage
            Assert.That(origMs, Is.Not.EqualTo(testMs));

            P2pNetDateTime.Now =() => new DateTime(testDT.Ticks);
            Assert.That(P2pNetDateTime.NowMs, Is.EqualTo(testMs));
            P2pNetDateTime.Now = () => DateTime.Now; // reset to default
        }
    }

}