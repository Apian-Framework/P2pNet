using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UniLog;
using static UniLog.UniLogger; // for SID()

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
            clockSync = new PeerClockSyncCalc(p2pId);
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
            public float offsetVariance;
            public float offsetSigma;

            public void LogStats(UniLogger logger, string statsName)
            {
                logger.Verbose($"*** Stats: {statsName}, Count: {sampleCount}, Offset: {avgOffsetMs}, OffsetSigma: {offsetSigma}, Lag: {avgLagMs}, LagSigma: {lagSigma}");
            }

        }

        public UniLogger logger;
        public string p2pId;
        protected TheStats currentStats;
        protected TheStats testStats; // using this during development to compare

        protected long avgSyncPeriodMs = 12000; // hard-coded start val (note there can be different channels w/different periods)

        public PeerClockSyncCalc(string _p2pId)
        {
            p2pId = _p2pId;
            logger = UniLogger.GetLogger("P2pNetSync");
            currentStats = new TheStats();
            testStats = new TheStats();
        }

        public long lastActivityMs; // so we know if we're currently syncing or have never synced - and unlike flags will
                                       // time out if a sync is interrupted

        public  long NetworkLagMs => currentStats.avgLagMs;// round trip time / 2
        public long ClockOffsetMs => currentStats.avgOffsetMs; // local systime + offset = remote peer's sysTime

        public void ReportInterimSyncProgress() { lastActivityMs = P2pNetDateTime.NowMs;}

        public bool ClockNeedsSync(int syncTimeoutMs)
        {
            avgSyncPeriodMs = (avgSyncPeriodMs + syncTimeoutMs) / 2; // running alpha=.5 ewma

            // Cause first 3 syncs to timeout quicker
            if (currentStats.sampleCount < 4)
                syncTimeoutMs = syncTimeoutMs / (5 - (int)currentStats.sampleCount); // (1,2,3) -> .25, .33 , .5

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

            // TODO: consider using T3 as timestamp for the latest sample


            // EWMA taking irregular sample timing into account
            // See: https://en.wikipedia.org/wiki/Moving_average#Application_to_measuring_computer_performance
            long dT =  P2pNetDateTime.NowMs - testStats.timeStampMs;
            long samplesPeriodMs = 4 * avgSyncPeriodMs; // TODO: revisit this. It might be ok
            UpdateStats( currentStats, IrregularPeriodlEwma, (dT,samplesPeriodMs), lag, theta);


            // Traditional (per-sample) EWMA - kinda assumes equal sample times.
            const long samplesN = 8;
            UpdateStats( testStats, TraditionalEwma, samplesN, lag, theta ); // Param is N in "N-sample moving avg"

            // Old "avg w/previous avg" (aplha = .5) EWMA
            //UpdateStats(  testStats, TerribleStupidEwma, 0, lag, theta ); // no param

            logger.Verbose($"*** Stats: Peer: {SID(p2pId)} NewOffset: {theta}, NewLag: {lag}");
            currentStats.LogStats(logger, "Current");
            testStats.LogStats(logger,    "   Test");
        }

        protected static void UpdateStats(TheStats statsInst, Func<long,long,float,long,object,(long,float)> avgFunc, object avgParam, long newLag, long newOffset)
        {
            // Stash current data
            statsInst.currentLag = newLag;
            statsInst.currentOffsetMs = newOffset;
            statsInst.timeStampMs = P2pNetDateTime.NowMs;

            (statsInst.avgLagMs, statsInst.lagVariance) =
                avgFunc(newLag, statsInst.avgLagMs, statsInst.lagVariance, statsInst.sampleCount, avgParam);

            (statsInst.avgOffsetMs, statsInst.offsetVariance) =
                avgFunc(newOffset, statsInst.avgOffsetMs, statsInst.offsetVariance, statsInst.sampleCount, avgParam);

            statsInst.lagSigma = (statsInst.lagVariance >= 0) ? (float)Math.Sqrt(statsInst.lagVariance) : -1f;
            statsInst.offsetSigma = (statsInst.offsetVariance >= 0) ? (float)Math.Sqrt(statsInst.offsetVariance) : -1f;

            statsInst.sampleCount++;
        }


        //  avg w/prev avg - lame-ass EWMA
        public static (long, float) TerribleStupidEwma(long newVal, long oldAvg,  float oldVariance, long sampleNum, object _noParam)
        {
            return (sampleNum == 0)
                ? (newVal,  -1f)
                : ( (newVal + oldAvg) / 2, -1f);
        }

        // normal, fixed-increment EWMA
        public static (long,float) TraditionalEwma(long newVal, long oldAvg, float oldVariance, long sampleNum, object avgOverSampleCountObj)
        {
            if (sampleNum == 0)
                return (newVal, 0);

            long avgOverSampleCount = (long)avgOverSampleCountObj;

            //  alpha is weignt of new sample
            float alpha = (sampleNum >= (avgOverSampleCount/2))
                        ? 2.0f / ((float)avgOverSampleCount - 1)   // use alpha calc
                        : 1.0f / (sampleNum+1);  // early on just average

            UniLogger.GetLogger("P2pNetSync").Debug($"*** Stats: alpha: {alpha}");

            float delta = newVal - oldAvg;
            long avg = oldAvg + (long)(alpha * delta);

            float variance = (1.0f - alpha) * (oldVariance + alpha * delta * delta);

            return (avg, variance);
        }


        public static (long,float) IrregularPeriodlEwma(long newVal, long oldAvg, float oldVariance, long sampleNum, object avgParams)
        {
            if (sampleNum == 0)
                return (newVal, 0);


            (long dT, long avgOverPeriodMs) = ( ValueTuple<long,long>)avgParams;

            //  alpha is weignt of new sample
            float alpha = (sampleNum < 5)
                        ?  1.0f / (sampleNum+1)  //  just average first 4 ( alpha = .5, .333, .25)
                        :  1.0f - (float)Math.Exp( -(double)dT / avgOverPeriodMs); // use alpha calc

            UniLogger.GetLogger("P2pNetSync").Debug($"*** Stats: alphaT: {alpha}");

            float delta = newVal - oldAvg;
            long avg = oldAvg + (long)(alpha * delta);

            float variance = (1.0f - alpha) * (oldVariance + alpha * delta * delta);

            return (avg, variance);
        }



    }

}
