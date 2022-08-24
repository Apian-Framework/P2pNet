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
            clockSync.CompleteSync(t0, t1, t2, t3);
        }
    }

    public class PeerClockSyncCalc
    {
        public class TheStats
        {
            // current measurements
            public long sampleCount;
            public long timeStampMs;
            public int currentLag; // the single data point
            public int currentOffsetMs; // local systime + offset = remote peer's sysTime

            // stats
            public double avgLagMs; // computed EWMA
            public double lagVariance;
            public double lagSigma;

            public double avgOffsetMs;
            public double offsetVariance;
            public double offsetSigma;

            public void LogStats(UniLogger logger, string statsName)
            {
                logger.Verbose($"*** Stats: {statsName}, Count: {sampleCount}, AvgOffset: {avgOffsetMs}, OffsetSigma: {offsetSigma}, AvgLag: {avgLagMs}, LagSigma: {lagSigma}");
            }

        }

        public UniLogger logger;
        public string p2pId;
        protected TheStats currentStats;
        protected TheStats testStats; // using this during development to compare

        protected double avgSyncPeriodMs = 15000; // hard-coded start val (note there can be different channels w/different periods)

        protected int initialSyncTimeoutMs = 200; // for 1st 4 syncTimeoutMs

        protected int jitterForNextTimeout;

        public long lastSyncCompletionMs; // Timestamp of last sync completion.
        public long lastSyncActivityMs; // By using this when testing we won't decide it's time a sync when
                                    // we are already in the middle of one


        public PeerClockSyncCalc(string _p2pId)
        {
            p2pId = _p2pId;
            logger = UniLogger.GetLogger("P2pNetSync");
            currentStats = new TheStats();
            testStats = new TheStats();
        }

        public  int NetworkLagMs => (int)Math.Round(currentStats.avgLagMs);// round trip time / 2
        public int ClockOffsetMs => (int)Math.Round(currentStats.avgOffsetMs); // local systime + offset = remote peer's sysTime

        public void ReportInterimSyncProgress() { lastSyncActivityMs = P2pNetDateTime.NowMs;}

        public bool ClockNeedsSync(int syncTimeoutMs)
        {
            // Cause first 3 syncs to timeout quicker
            if (currentStats.sampleCount < 4)
                syncTimeoutMs = initialSyncTimeoutMs;

            return
                lastSyncActivityMs == 0 // has never synced
                || P2pNetDateTime.NowMs-lastSyncActivityMs > syncTimeoutMs + jitterForNextTimeout; // or we havent started or participated in a sync in too long.
        }

        public PeerClockSyncInfo GetClockSyncInfo(string p2pId)
        {
            // TODO: really create a new one every call?
            return new PeerClockSyncInfo(p2pId,  currentStats.sampleCount,  (int)(P2pNetDateTime.NowMs - currentStats.timeStampMs),
                (int)currentStats.avgOffsetMs, currentStats.offsetSigma,  (int)currentStats.avgLagMs, currentStats.lagSigma);
        }
     //public PeerClockSyncInfo(string pid, long cnt, long since, long offset, double offsetSigma, long lag, double lagSigma)

        public void CompleteSync(long t0, long t1, long t2, long t3)
        {
            // basic immediate clacs
            int theta = (int)((t1 - t0) + (t2-t3)) / 2; // offset
            int lag = (int)((t3 - t0) - (t2-t1)) / 2;

            long msSincePreviousCompletion = lastSyncCompletionMs == 0 ? 0 : (P2pNetDateTime.NowMs - lastSyncCompletionMs);

            // not so sure about this value.
            avgSyncPeriodMs = ((avgSyncPeriodMs + msSincePreviousCompletion) * .5f); // running alpha=.5 ewma

            jitterForNextTimeout = new Random().Next((int)msSincePreviousCompletion/4);
            lastSyncCompletionMs = P2pNetDateTime.NowMs;

            // TODO: consider using T3 as timestamp for the latest sample

            TheStats theStats = currentStats;

            // Traditional (per-sample) EWMA - kinda assumes equal sample times.
            // 2 sigma lag outlier rejection
            const long samplesN = 8;
            if ( Math.Abs((theta - theStats.avgOffsetMs)) > 2*theStats.offsetSigma && theStats.sampleCount > 6)
            {
                logger.Warn($"!!! CompleteSync() - Offset {theta} is outside tolerance: ({theStats.avgOffsetMs-2*theStats.offsetSigma}, {theStats.avgOffsetMs+2*theStats.offsetSigma}) Ignoring sample");
            } else if (Math.Abs(lag - theStats.avgLagMs) > 2*theStats.lagSigma  && theStats.sampleCount > 8) {
                logger.Warn($"!!! CompleteSync() - Lag {lag} is outside tolerance:  (0, {theStats.avgLagMs+2*theStats.lagSigma}) Ignoring sample");
            } else {
                UpdateStats( theStats, TraditionalEwma, samplesN, lag, theta ); // Param is N in "N-sample moving avg
            }

            theStats = testStats;
            if ( Math.Abs((theta - theStats.avgOffsetMs)) > 2*theStats.offsetSigma && theStats.sampleCount > 6)
            {
                logger.Warn($"!!! CompleteSync() - Test offset {theta} is outside tolerance: ({theStats.avgOffsetMs-2*theStats.offsetSigma}, {theStats.avgOffsetMs+2*theStats.offsetSigma}) Ignoring sample");
            } else if (Math.Abs(lag - theStats.avgLagMs) > 2*theStats.lagSigma  && theStats.sampleCount > 8) {
                logger.Warn($"!!! CompleteSync() - Test Lag {lag} is outside tolerance:  (0, {theStats.avgLagMs+2*theStats.lagSigma}) Ignoring sample");
            } else {
               UpdateStats(theStats, ScatterShot, samplesN, lag, theta );
            }

            //  theStats = testStats;
            /// Traditional (per-sample) EWMA - kinda assumes equal sample times.
            // UpdateStats( theStats, TraditionalEwma, samplesN, lag, theta ); // Param is N in "N-sample moving avg"

            // EWMA taking irregular sample timing into account
            // See: https://en.wikipedia.org/wiki/Moving_average#Application_to_measuring_computer_performance

            // theStats = testStats;
            // int dT =  P2pNetDateTime.NowMs - testStats.timeStampMs;
            // int samplesPeriodMs = 3 * avgSyncPeriodMs; // TODO: revisit this. It might be ok
            // UpdateStats( testStats, IrregularPeriodlEwma, (dT,samplesPeriodMs), lag, theta);

            // Old "avg w/previous avg" (aplha = .5) EWMA
            //UpdateStats(  testStats, TerribleStupidEwma, 0, lag, theta ); // no param

            logger.Verbose($"*** Stats: Peer: {SID(p2pId)} CurOffset: {theta}, CurLag: {lag}");
            currentStats.LogStats(logger, "Current");
            testStats.LogStats(logger,    "   Test");
        }

        protected static void UpdateStats(TheStats statsInst, Func<long,double,double,long,object,(double,double)> avgFunc, object avgParam, int newLag, int newOffset)
        {
            // Stash current data
            statsInst.currentLag = newLag;
            statsInst.currentOffsetMs = newOffset;
            statsInst.timeStampMs = P2pNetDateTime.NowMs;

            (statsInst.avgLagMs, statsInst.lagVariance) =
                avgFunc(newLag, statsInst.avgLagMs, statsInst.lagVariance, statsInst.sampleCount, avgParam);

            (statsInst.avgOffsetMs, statsInst.offsetVariance) =
                avgFunc(newOffset, statsInst.avgOffsetMs, statsInst.offsetVariance, statsInst.sampleCount, avgParam);

            statsInst.lagSigma = (statsInst.lagVariance >= 0) ? Math.Sqrt(statsInst.lagVariance) : -1;
            statsInst.offsetSigma = (statsInst.offsetVariance >= 0) ? Math.Sqrt(statsInst.offsetVariance) : -1;

            statsInst.sampleCount++;
        }


        //  avg w/prev avg - lame-ass EWMA
        public static (double, double) TerribleStupidEwma(long newVal, double oldAvg,  double oldVariance, long sampleNum, object _noParam)
        {
            return (sampleNum == 0)
                ? (newVal,  -1)
                : ( (newVal + oldAvg) / 2, -1);
        }



        // A simple mean value for both offset and lag.
        // This might actually be the most defensible method since in reality all of these clocks are running
        // at the same rate so the actual offset is a constant.
        public static (double, double) ScatterShot(long newVal, double oldAvg,  double oldVariance, long sampleNum, object _noParam)
        {
            double avg = (oldAvg * sampleNum + newVal) /(sampleNum+1);

            double delta = newVal - oldAvg;
            double variance = (oldVariance * sampleNum + delta*delta) /(sampleNum+1);
            return (avg, variance);
        }


        // normal, fixed-increment EWMA
        public static (double,double) TraditionalEwma(long newVal, double oldAvg, double oldVariance, long sampleNum, object avgOverSampleCountObj)
        {
            if (sampleNum == 0)
                return (newVal, 0);

            long avgOverSampleCount = (long)avgOverSampleCountObj;

            //  alpha is weignt of new sample
            double alpha = (sampleNum >= (avgOverSampleCount/2))
                        ? 2.0 / ((double)avgOverSampleCount - 1)   // use alpha calc
                        : 1.0 / (sampleNum+1);  // early on just average

            UniLogger.GetLogger("P2pNetSync").Debug($"*** Stats: alpha: {alpha}");

            double delta = newVal - oldAvg;
            double avg = oldAvg + (alpha * delta);

            double variance = (1.0 - alpha) * (oldVariance + alpha * delta * delta);

            return (avg, variance);
        }

         public static (double,double) IrregularPeriodlEwma(long newVal, double oldAvg, double oldVariance, long sampleNum, object avgParams)
        {
            if (sampleNum == 0)
                return (newVal, 0);


            (int dT, int avgOverPeriodMs) = ( ValueTuple<int,int>)avgParams;

            //  alpha is weignt of new sample
            double alpha = (sampleNum < 4)
                        ?  1.0 / (sampleNum+1)  //  just average first 4 ( alpha = 1 (sampleNum == 0), .5, .333, .25)
                        :  1.0 - Math.Exp( - (double)dT / avgOverPeriodMs); // use alpha calc

            UniLogger.GetLogger("P2pNetSync").Debug($"*** Stats: avgOverPeriodMs: {avgOverPeriodMs}");
            UniLogger.GetLogger("P2pNetSync").Debug($"*** Stats: alphaT: {alpha}");

            double delta = newVal - oldAvg;
            double avg = oldAvg + (long)(alpha * delta);

            double variance = (1.0 - alpha) * (oldVariance + alpha * delta * delta);

            return (avg, variance);
        }



    }

}
