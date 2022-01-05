using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UniLog;

namespace P2pNet
{
    // ReSharper disable InconsistentNaming
    // Problem here is that "p2p" is a word: "peer-to-peer" and the default .NET ReSharper rules dealing with digits result
    // in dumb stuff, like a field called "_p2PFooBar" with the 2nd P capped.

    public class P2pNetPeer
    {
        public string p2pId;

        public PeerClockSyncCalc clockSync;

        public P2pNetPeer(string _p2pId)
        {
            p2pId = _p2pId;
            clockSync = new PeerClockSyncCalc();
        }

        // Ping/Timeout
        public long LastHeardFromTs {get; protected set; } // time when last heard from. 0 for never heard from
        public long LastSentToTs {get; protected set; } // stamp for last message we sent (use to throttle pings somewhat)

        public void UpdateLastHeardFrom() =>  LastHeardFromTs = P2pNetDateTime.NowMs;
        public void UpdateLastSentTo() =>  LastSentToTs = P2pNetDateTime.NowMs;

        // Clock sync
        public PeerClockSyncInfo ClockSyncInfo => clockSync.GetClockSyncInfo(p2pId); // why not just make this on Compute?

        //public  long NetworkLagMs => clockSync.NetworkLagMs;
        //public long ClockOffsetMs => clockSync.ClockOffsetMs;

        public bool ClockNeedsSync(int syncTimeoutMs) => clockSync.ClockNeedsSync(syncTimeoutMs);
        public void ReportInterimSyncProgress() => clockSync.ReportInterimSyncProgress(); // Call when performing sync steps
        public void CompleteClockSync(long t0, long t1, long t2, long t3) // Call when sync is finished.
        {
            ReportInterimSyncProgress();
             clockSync.Compute(t0, t1, t2, t3);
        }
    }

    public class PeerClockSyncCalc
    {
        public class TheStats
        {
            // current measurements
            public long sampleCount;
            public long timeStampMs;
            public long currentLag; // the single data point
            public long currentOffsetMs; // local systime + offset = remote peer's sysTime

            // stats
            public long avgLagMs; // computed EWMA
            public float lagVariance;
            public float lagSigma;

            public long avgOffsetMs;

        }

        protected TheStats currentStats;

        public PeerClockSyncCalc()
        {
            currentStats = new TheStats();
            currentStats.sampleCount = 0;
        }

        protected long lastActivityMs; // so we know if we're currently syncing or have never synced - and unlike flags will
                                       // time out if a sync is interrupted

        public  long NetworkLagMs => currentStats.avgLagMs;// round trip time / 2
        public long ClockOffsetMs => currentStats.avgOffsetMs; // local systime + offset = remote peer's sysTime

        public void ReportInterimSyncProgress() { lastActivityMs = P2pNetDateTime.NowMs;}

        public bool ClockNeedsSync(int syncTimeoutMs)
        {
            return
                lastActivityMs == 0 // has never synced
                || P2pNetDateTime.NowMs-lastActivityMs > syncTimeoutMs; // or we havent start or participated in a sync in too long.
        }

        public PeerClockSyncInfo GetClockSyncInfo(string p2pId)
        {
            // TODO: really create a new one every call?
            return new PeerClockSyncInfo(p2pId, P2pNetDateTime.NowMs - currentStats.timeStampMs, currentStats.avgOffsetMs, currentStats.avgLagMs);
        }

        public void Compute(long t0, long t1, long t2, long t3)
        {
            long theta = ((t1 - t0) + (t2-t3)) / 2; // offset
            long lag = ((t3 - t0) - (t2-t1)) / 2;

            // Stash current data
            currentStats.currentLag = lag;
            currentStats.currentOffsetMs = theta;
            currentStats.timeStampMs = P2pNetDateTime.NowMs;

            // Set if unset, else compute
            currentStats.avgOffsetMs = (currentStats.sampleCount == 0) ? theta : TerribleStupidEwma(theta, currentStats.avgOffsetMs);
            currentStats.avgLagMs = (NetworkLagMs == 0) ? lag : TerribleStupidEwma(lag,  currentStats.avgLagMs);

            currentStats.sampleCount++;
        }


        //  avg w/prev avg - lame-ass EWMA
        protected long TerribleStupidEwma(long newVal, long oldAvg)
        {
            return (newVal + oldAvg) / 2;
        }



    }

}
