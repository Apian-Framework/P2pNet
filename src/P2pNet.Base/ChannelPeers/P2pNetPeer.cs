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
        public string p2pId { get; private set; } // This is always created unique when a local peer is created, and picked up by other peers

        public string p2pAddress {get; private set; }

        public PeerClockSyncCalc clockSync;

        public P2pNetPeer( string _p2pId)
        {
            p2pId = _p2pId;
            clockSync = new PeerClockSyncCalc(p2pId);
            // p2pAddress is null until set during HELLO
        }

        public void SetAddress(string _p2pAddress)
        {
             p2pAddress = _p2pAddress;
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

        public class TrackedValue
        {
            public double avgVal;
            public double sigma;  // standard deviation

            // accumulators, etc.
            public double varianceAccum; // use depends on VarianceMethod

        }

        public class TheStats
        {
            public enum StatsVarianceMethod
            {
                EWMA, //https://en.wikipedia.org/wiki/Moving_average#Exponentially_weighted_moving_variance_and_standard_deviation
                Welford // https://en.wikipedia.org/wiki/Algorithms_for_calculating_variance#Welford's_online_algorithm
            }

            public string displayName; // really only needed when testing/comparing

            // current measurements
            public long timeStampMs;
            public int currentLag; // the single data point
            public int currentOffsetMs; // local systime + offset = remote peer's sysTime
            public long sampleCount; // accepted samples. For values
            public long varianceSampleCount; // rejected samples are included when calculating variance

            public TrackedValue clockOffset;
            public TrackedValue netLag;

            public Func<long,double,double,long,object,(double,double)> AvgFunc;
            public object AvgFuncParams;
            public StatsVarianceMethod VarianceMethod;

            public TheStats(string name, Func<long,double,double,long,object,(double,double)> avgFunc, object avgFuncParams, StatsVarianceMethod varianceMethod)
            {
                displayName = name;
                AvgFunc = avgFunc;
                AvgFuncParams = avgFuncParams;
                VarianceMethod = varianceMethod;
                clockOffset = new TrackedValue();
                netLag = new TrackedValue();
            }

            // stats
            public void LogStats(UniLogger logger, string prefixMsg)
            {
                logger.Verbose($"*** Stats: {prefixMsg} Count: {sampleCount}/{varianceSampleCount} , Offset: {clockOffset.avgVal:F3}, OffsetSigma: {clockOffset.sigma:F3}, Lag: {netLag.avgVal:F3}, LagSigma: {netLag.sigma:F3}, Name: {displayName}");
            }

        }

        public UniLogger logger;
        public string p2pId;
        protected TheStats currentStats;
        protected IList<TheStats> testStatsList; // using this during development to compare

        protected int avgSyncPeriodMs = -1; // gets set to caller-requested period the first time through
        protected int currentSyncPeriodMs = -1; //

        protected int initialSyncTimeoutMs = 200; // for 1st 4 syncTimeoutMs

        protected int jitterForNextTimeout;

        public long lastSyncCompletionMs; // Timestamp of last sync completion.
        public long lastSyncActivityMs; // By using this when testing we won't decide it's time a sync when
                                    // we are already in the middle of one


        public PeerClockSyncCalc(string _p2pId)
        {
            p2pId = _p2pId;
            logger = UniLogger.GetLogger("P2pNetSync");
            currentStats = new TheStats("IrregularPeriodEwma", IrregularPeriodEwma, 3, TheStats.StatsVarianceMethod.EWMA); // param is N where "sampled over" time is N*avgSamplePerod

            testStatsList = new List<TheStats>();
            //testStatsList.Add( new TheStats("TraditionalEwma(8)", TraditionalEwma, 8, TheStats.StatsVarianceMethod.EWMA) );
            // testStatsList.Add(new TheStats("IrregularPeriodlEwma", IrregularPeriodlEwma, null, TheStats.StatsVarianceMethod.EWMA) ); // Param is N in "N-sample moving avg"
            //testStatsList.Add( new TheStats("JustAllMean", JustAllMean, null,  TheStats.StatsVarianceMethod.Welford ) );

            lastSyncActivityMs = P2pNetDateTime.NowMs; // delay a little before sending a sync request. Trying to avoid both peers sending.
            jitterForNextTimeout = new Random().Next((int)initialSyncTimeoutMs); //
        }

        public  int NetworkLagMs => (int)Math.Round(currentStats.netLag.avgVal);// round trip time / 2
        public int ClockOffsetMs => (int)Math.Round(currentStats.clockOffset.avgVal); // local systime + offset = remote peer's sysTime

        public void ReportInterimSyncProgress() { lastSyncActivityMs = P2pNetDateTime.NowMs;}

        public bool ClockNeedsSync(int syncTimeoutMs)
        {
            // Cause first 3 syncs to timeout quicker
            if (currentStats.sampleCount < 4)
                syncTimeoutMs = initialSyncTimeoutMs;

            return P2pNetDateTime.NowMs-lastSyncActivityMs > syncTimeoutMs + jitterForNextTimeout;

            //    lastSyncActivityMs == 0 // has never synced
            //    || P2pNetDateTime.NowMs-lastSyncActivityMs > syncTimeoutMs + jitterForNextTimeout; // or we havent started or participated in a sync in too long.
        }

        public PeerClockSyncInfo GetClockSyncInfo(string p2pId)
        {
            // TODO: really create a new one every call?
            return new PeerClockSyncInfo(p2pId,  currentStats.sampleCount,  (int)(P2pNetDateTime.NowMs - currentStats.timeStampMs),
                (int)Math.Round(currentStats.clockOffset.avgVal), currentStats.clockOffset.sigma,  (int)Math.Round(currentStats.netLag.avgVal), currentStats.netLag.sigma);
        }
     //public PeerClockSyncInfo(string pid, long cnt, long since, long offset, double offsetSigma, long lag, double lagSigma)

        public void CompleteSync(long t0, long t1, long t2, long t3)
        {
            // basic immediate clacs
            int offset = (int)((t1 - t0) + (t2-t3)) / 2; // offset
            int lag = (int)((t3 - t0) - (t2-t1)) / 2;

            currentSyncPeriodMs = lastSyncCompletionMs == 0 ? 0 : (int)(P2pNetDateTime.NowMs - lastSyncCompletionMs);
            // not so sure about this value.
            avgSyncPeriodMs = (avgSyncPeriodMs > 0) ? ((avgSyncPeriodMs + currentSyncPeriodMs) / 2) : currentSyncPeriodMs; // running alpha=.5 ewma

           logger.Verbose($"*** Stats: CurSyncPeriod: {currentSyncPeriodMs} AvgSyncPeriod: {avgSyncPeriodMs}");

            jitterForNextTimeout = new Random().Next((int)currentSyncPeriodMs/4);
            lastSyncCompletionMs = P2pNetDateTime.NowMs;

            // TODO: consider using T3 as timestamp for the latest sample

            List<TheStats> statsSets = new List<TheStats>{currentStats}; // TODO: create this in ctor
            statsSets.AddRange(testStatsList);

            foreach ( TheStats theStats in statsSets )
            {
                // This is iterating over "currentStats" + everything in testStatsList in order to compare.
                // In production we only need currentstats

                // Get rid of te  hard-coded "6"s below (min sample count for variance checking/rejection)
                bool sampleAccepted = true;
                if ( Math.Abs((offset - theStats.clockOffset.avgVal)) > 2*theStats.clockOffset.sigma && theStats.sampleCount > 6)
                {
                    sampleAccepted = false;
                    logger.Warn($"!!! CompleteSync() - Offset {offset} is outside tolerance: ({theStats.clockOffset.avgVal-2*theStats.clockOffset.sigma}, {theStats.clockOffset.avgVal+2*theStats.clockOffset.sigma}) Ignoring sample");
                } else if ((lag - theStats.netLag.avgVal) > 2*theStats.netLag.sigma  && theStats.sampleCount > 6) {
                    sampleAccepted = false;
                    logger.Warn($"!!! CompleteSync() - Lag {lag} is outside tolerance:  (0, {theStats.netLag.avgVal+2*theStats.netLag.sigma}) Ignoring sample");
                }

                UpdateStats( theStats, offset, lag, sampleAccepted); // always run the update since variance is ALWAYS calculated
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

            logger.Info($"*** CompleteSync() Stats: Peer: {SID(p2pId)} CurOffset: {offset}, CurLag: {lag}");
            currentStats.LogStats(logger, "Current:");
            foreach (TheStats ts in testStatsList)
                ts.LogStats(logger,    "   Test:");
        }

        protected void UpdateStats(TheStats statsInst, int newOffset,  int newLag, bool sampleAccepted)
        {
            // Stash current data
            statsInst.currentLag = newLag;
            statsInst.currentOffsetMs = newOffset;
            statsInst.timeStampMs = P2pNetDateTime.NowMs;

            (TrackedValue, long)[] trackedValues = { (statsInst.clockOffset, newOffset), (statsInst.netLag, newLag) };
            foreach ( (TrackedValue tVal, long newVal) in trackedValues )
            {
                double maybeNewAvg;

                (maybeNewAvg, tVal.varianceAccum) =
                    statsInst.AvgFunc(newVal, tVal.avgVal, tVal.varianceAccum, statsInst.sampleCount, statsInst.AvgFuncParams);

                if (sampleAccepted)
                    tVal.avgVal = maybeNewAvg;

                switch (statsInst.VarianceMethod )
                {
                    case TheStats.StatsVarianceMethod.Welford:
                        tVal.sigma =  (statsInst.varianceSampleCount > 0) ? Math.Sqrt(tVal.varianceAccum / statsInst.varianceSampleCount) : 0;
                        break;
                    case TheStats.StatsVarianceMethod.EWMA:
                        tVal.sigma =  (statsInst.varianceSampleCount > 0) ? Math.Sqrt(tVal.varianceAccum) : 0; // Variance is a runing val
                        break;
                }
            }

            statsInst.varianceSampleCount++; // variance is always calculated. If only accepted samples are included, real changes in value will always get rejectde

            if (sampleAccepted)
                statsInst.sampleCount++;  // if sample not accepted (value not used) then don't increment sampleCount
        }


        //  avg w/prev avg - lame-ass EWMA
        // public static (double, double) TerribleStupidEwma(long newVal, double oldAvg,  double oldAggrVariance, long sampleCnt, object _noParam)
        // {
        //     return (sampleCnt == 0)
        //         ? (newVal,  -1)
        //         : ( (newVal + oldAvg) / 2, -1);
        // }

        // A simple mean of ALL values for both offset and lag.
        // This might be a pretty defensible method since in reality all of these clocks are running
        // at the same rate so the actual offset is really a constant.
        public (double, double) JustAllMean(long newVal, double curAvg,  double curAggrVariance, long curSampleCnt, object _noParam)
        {
            // sample count is incremented when this data is applied by the calling func
            double delta = newVal - curAvg;
            double avg = curAvg  + delta /(curSampleCnt+1);
            double aggrVariance = curSampleCnt > 0 ? curAggrVariance + delta*delta : 0;  // variance for first value is 0
            return (avg, aggrVariance);
        }


        // normal, fixed-increment EWMA
        public (double,double) TraditionalEwma(long newVal, double curAvg, double curRunningVariance, long curSampleCnt, object avgParams)
        {
            if (curSampleCnt == 0)
                return (newVal, 0);

            int avgOverSampleCount = (int)avgParams;

            //  alpha is weignt of new sample
            double alpha = (curSampleCnt >= (avgOverSampleCount/2))
                        ? 2.0 / ((double)avgOverSampleCount - 1)   // this is the nominal alpha calculation
                        : 1.0 / (curSampleCnt+1);  // early on just average

            UniLogger.GetLogger("P2pNetSync").Debug($"*** Stats: alpha: {alpha}");

            // see: https://en.wikipedia.org/wiki/Moving_average#Exponentially_weighted_moving_variance_and_standard_deviation

            double delta = newVal - curAvg;
            double avg = curAvg + alpha * delta;

            double runningVariance =  curSampleCnt > 0 ? (1.0 - alpha) * (curRunningVariance + alpha * delta * delta) : 0; // variance for first value is 0

            return (avg, runningVariance);
        }

// - -------- Below here still needs updating to using aggRVariance


        public (double,double) IrregularPeriodEwma(long newVal, double curAvg, double curRunningVariance, long curSampleCnt, object multiplierObj)
        {
            // https://en.wikipedia.org/wiki/Moving_average#Application_to_measuring_computer_performance

            if (curSampleCnt == 0)
                return (newVal, 0);

            // dT is time between ths sample and the previous one
            // avgOverPeriodMs is the time in ms over which the reading is said to be averaged
            int dT = currentSyncPeriodMs;
            int avgOverPeriodMs = (int)multiplierObj * avgSyncPeriodMs;

            //  alpha is weignt of new sample
            double alpha = (curSampleCnt < 4)
                        ?  1.0 / (curSampleCnt+1)  //  just average first 4 ( alpha = 1 (sampleNum == 0), .5, .333, .25)
                        :  1.0 - Math.Exp( - (double)dT / avgOverPeriodMs); // nomical alpha calculation

            UniLogger.GetLogger("P2pNetSync").Debug($"*** Stats: avgOverPeriodMs: {avgOverPeriodMs}");
            UniLogger.GetLogger("P2pNetSync").Debug($"*** Stats: alpha: {alpha}");

            // see: https://en.wikipedia.org/wiki/Moving_average#Exponentially_weighted_moving_variance_and_standard_deviation

            double delta = newVal - curAvg;
            double avg = curAvg + alpha * delta;

            double variance =  curSampleCnt > 0 ? (1.0 - alpha) * (curRunningVariance + alpha * delta * delta) : 0; // variance for first value is 0

            return (avg, variance);
        }



    }

}
