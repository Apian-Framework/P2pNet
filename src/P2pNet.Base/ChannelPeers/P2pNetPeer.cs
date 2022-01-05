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
        public void ReportSyncProgress() => clockSync.ReportSyncProgress();
        public void ComputeClockSync(long t0, long t1, long t2, long t3) => clockSync.Compute(t0, t1, t2, t3);
    }

    public class PeerClockSyncCalc
    {
        public const int kNeverSynced = -1;
        public const int kSyncInProgress = -2;
        protected long lastClockSyncMs; // so we know if we're syncing

        public  long NetworkLagMs {get; private set;} // round trip time / 2
        public long ClockOffsetMs {get; private set;}// local systime + offset = remote peer's sysTime

        public long MsSinceClockSync { get => lastClockSyncMs == 0 ? kNeverSynced : (CurrentlySyncing ? kSyncInProgress :   P2pNetDateTime.NowMs - lastClockSyncMs); }

        public PeerClockSyncInfo GetClockSyncInfo(string p2pId) => new PeerClockSyncInfo(p2pId, MsSinceClockSync, ClockOffsetMs, NetworkLagMs);

        public void ReportSyncProgress() { lastClockSyncMs = -P2pNetDateTime.NowMs;} // negative means in progress
        public bool CurrentlySyncing => lastClockSyncMs < 0;
        public bool ClockNeedsSync(int syncTimeoutMs)
        {
            // either not set or too long ago and not currently in progress
            return lastClockSyncMs == 0
                || (lastClockSyncMs > 0 && P2pNetDateTime.NowMs-lastClockSyncMs > syncTimeoutMs);
        }

        //FIXME: move to clocksyncdata class
        public void Compute(long t0, long t1, long t2, long t3)
        {
            long theta = ((t1 - t0) + (t2-t3)) / 2; // offset
            long lag = ((t3 - t0) - (t2-t1)) / 2;

            // Set if unset, else avg w/prev value
            ClockOffsetMs = (ClockOffsetMs == 0) ? theta : (theta + ClockOffsetMs) / 2;
            NetworkLagMs = (NetworkLagMs == 0) ? lag : (lag + NetworkLagMs) / 2;
            lastClockSyncMs = P2pNetDateTime.NowMs;
        }
    }

}
