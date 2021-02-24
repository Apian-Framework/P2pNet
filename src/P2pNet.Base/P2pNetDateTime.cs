using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UniLog;

namespace P2pNet
{
    public static class P2pNetDateTime
    {
        // By default P2pNetDateTime.Now() returns the static DateTime.Now "current system time" property
        // In a test environment, however, a call like:
        //
        // P2pNetDateTime.Now = () => new DateTime(2000,1,1);
        //
        // causes it to return the specified DateTime until otherwise directed
        public static Func<DateTime> Now = () => DateTime.Now;

        // This is what is typically used by P2pNet code:
        public static long NowMs => Now().Ticks / TimeSpan.TicksPerMillisecond;
    }

}
