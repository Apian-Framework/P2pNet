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
            public double lagAggrVariance;  // this, sometimes called M2, M2 is the aggregate (running sum) of the squares of
                                 // all of the the (value - meanBeforeValueApplied) "deltas"
                                 // See https://en.wikipedia.org/wiki/Algorithms_for_calculating_variance#Welford's_online_algorithm
            public double lagSigma; // standard variation: sqrt( aggrVariance / sampleCnt)
            public double lagM2;

            public double avgOffsetMs;
            public double offsetAggrVariance;
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
            int offset = (int)((t1 - t0) + (t2-t3)) / 2; // offset
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

            const long samplesN = 8; // samples to average over
            if ( Math.Abs((offset - theStats.avgOffsetMs)) > 2*theStats.offsetSigma && theStats.sampleCount > 6)
            {
                logger.Warn($"!!! CompleteSync() - Offset {offset} is outside tolerance: ({theStats.avgOffsetMs-2*theStats.offsetSigma}, {theStats.avgOffsetMs+2*theStats.offsetSigma}) Ignoring sample");
            } else if ((lag - theStats.avgLagMs) > 2*theStats.lagSigma  && theStats.sampleCount > 8) {
                logger.Warn($"!!! CompleteSync() - Lag {lag} is outside tolerance:  (0, {theStats.avgLagMs+2*theStats.lagSigma}) Ignoring sample");
            } else {
                UpdateStats( theStats, TraditionalEwma, offset, lag, samplesN );
            }

            theStats = testStats;
            if ( Math.Abs((offset - theStats.avgOffsetMs)) > 2*theStats.offsetSigma && theStats.sampleCount > 6)
            {
                logger.Warn($"!!! CompleteSync() - Test offset {offset} is outside tolerance: ({theStats.avgOffsetMs-2*theStats.offsetSigma}, {theStats.avgOffsetMs+2*theStats.offsetSigma}) Ignoring sample");
            } else if ((lag - theStats.avgLagMs) > 2*theStats.lagSigma  && theStats.sampleCount > 8) {
                logger.Warn($"!!! CompleteSync() - Test Lag {lag} is outside tolerance:  (0, {theStats.avgLagMs+2*theStats.lagSigma}) Ignoring sample");
            } else {
               UpdateStats(theStats, JustMean, offset, lag );
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

            logger.Verbose($"*** Stats: Peer: {SID(p2pId)} CurOffset: {offset}, CurLag: {lag}");
            currentStats.LogStats(logger, "Current");
            testStats.LogStats(logger,    "   Test");
        }

        protected static void UpdateStats(TheStats statsInst, Func<long,double,double,long,object,(double,double)> avgFunc, int newOffset,  int newLag, object avgFuncParams = null)
        {
            // Stash current data
            statsInst.currentLag = newLag;
            statsInst.currentOffsetMs = newOffset;
            statsInst.timeStampMs = P2pNetDateTime.NowMs;

            // sys clock offset
            (statsInst.avgOffsetMs, statsInst.offsetAggrVariance) =
                avgFunc(newOffset, statsInst.avgOffsetMs, statsInst.offsetAggrVariance, statsInst.sampleCount, avgFuncParams);

            // packet lag
            (statsInst.avgLagMs, statsInst.lagAggrVariance) =
                avgFunc(newLag, statsInst.avgLagMs, statsInst.lagAggrVariance, statsInst.sampleCount, avgFuncParams);

            // Should we check to enqure  aggrVarinace > 0?
            statsInst.offsetSigma = (statsInst.sampleCount > 0) ? Math.Sqrt(statsInst.offsetAggrVariance / statsInst.sampleCount) : -1;

            statsInst.lagSigma = (statsInst.sampleCount > 0) ? Math.Sqrt(statsInst.lagAggrVariance / statsInst.sampleCount) : -1;

            statsInst.sampleCount++;
        }


        //  avg w/prev avg - lame-ass EWMA
        public static (double, double) TerribleStupidEwma(long newVal, double oldAvg,  double oldAggrVariance, long sampleCnt, object _noParam)
        {
            return (sampleCnt == 0)
                ? (newVal,  -1)
                : ( (newVal + oldAvg) / 2, -1);
        }



        // A simple mean value for both offset and lag.
        // This might actually be the most defensible method since in reality all of these clocks are running
        // at the same rate so the actual offset is a constant.
        public static (double, double) JustMean(long newVal, double curAvg,  double curAggrVariance, long curSampleCnt, object _noParam)
        {
            // sample count is incremented when this data is applied by the calling func

            double delta = newVal - curAvg; // avg before newVal is applied
            double avg = curAvg  + delta /(curSampleCnt+1);
            double aggrVariance = curAggrVariance + delta*delta;
            return (avg, aggrVariance);
        }


        // normal, fixed-increment EWMA
        public static (double,double) TraditionalEwma(long newVal, double curAvg, double curAggrVariance, long curSampleCnt, object avgOverSampleCountObj)
        {
            if (curSampleCnt == 0)
                return (newVal, 0);

            long avgOverSampleCount = (long)avgOverSampleCountObj; // number of samples to avg over

            //  alpha is weignt of new sample
            double alpha = (curSampleCnt >= (avgOverSampleCount/2))
                        ? 2.0 / ((double)avgOverSampleCount - 1)   // use alpha calc
                        : 1.0 / (curSampleCnt+1);  // early on just average

            UniLogger.GetLogger("P2pNetSync").Debug($"*** Stats: alpha: {alpha}");

            // see: https://en.wikipedia.org/wiki/Moving_average#Exponentially_weighted_moving_variance_and_standard_deviation
            double delta = newVal - curAvg;
            double avg = curAvg + alpha * delta;

            double curVariance = curAggrVariance / curSampleCnt;
            double newVariance = (1.0 - alpha) * (curVariance + alpha * delta * delta);
            double aggrVariance = newVariance * (curSampleCnt+1); // This is super-fugly... does it even work?

            return (avg, aggrVariance);
        }

// - -------- Below here still needs updating to using aggRVariance

        public static (double,double) TraditionalEwma2(long newVal, double curAvg, double curVariance, long sampleCnt, object avgOverSampleCountObj)
        {

            if (sampleCnt == 0)
                return (newVal, 0);

            long avgOverSampleCount = (long)avgOverSampleCountObj; // number of samples to avg over

            //  alpha is weignt of new sample
            double alpha = (sampleCnt >= (avgOverSampleCount/2))
                        ? 2.0 / ((double)avgOverSampleCount - 1)   // use alpha calc
                        : 1.0 / (sampleCnt+1);  // early on just average

            UniLogger.GetLogger("P2pNetSync").Debug($"*** Stats: alpha: {alpha}");

            double delta = newVal - curAvg;
            double avg = curAvg + (alpha * delta);
            double variance = (1.0 - alpha) * (curVariance + alpha * delta * delta);


            return (avg, variance);
        }

         public static (double,double) IrregularPeriodlEwma(long newVal, double curAvg, double curVariance, long sampleCnt, object avgParams)
        {
            if (sampleCnt == 0)
                return (newVal, 0);


            (int dT, int avgOverPeriodMs) = ( ValueTuple<int,int>)avgParams;

            //  alpha is weignt of new sample
            double alpha = (sampleCnt < 4)
                        ?  1.0 / (sampleCnt+1)  //  just average first 4 ( alpha = 1 (sampleNum == 0), .5, .333, .25)
                        :  1.0 - Math.Exp( - (double)dT / avgOverPeriodMs); // use alpha calc

            UniLogger.GetLogger("P2pNetSync").Debug($"*** Stats: avgOverPeriodMs: {avgOverPeriodMs}");
            UniLogger.GetLogger("P2pNetSync").Debug($"*** Stats: alphaT: {alpha}");

            // see: https://en.wikipedia.org/wiki/Moving_average#Exponentially_weighted_moving_variance_and_standard_deviation

            double delta = newVal - curAvg;
            double avg = curAvg + alpha * delta;

            double variance = (1.0 - alpha) * (curVariance + alpha * delta * delta);

            return (avg, variance);
        }



    }

}
