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

    public class PeerClockSyncData
    {
        // ReSharper disable MemberCanBePrivate.Global
        public string peerId;
        public long networkLagMs; // round trip time / 2
        public long clockOffsetMs; // localTime + offset = peerTime
        public long msSinceLastSync;
        public PeerClockSyncData(string pid, long since, long offset, long lag)
        {
            peerId = pid;
            msSinceLastSync = since;
            networkLagMs = lag;
            clockOffsetMs = offset;
        }
    }

    public class P2pNetPeer
    {
        public string p2pId;

        protected long lastClockSyncMs; // so we know if we're syncing

        public  long NetworkLagMs {get; private set;} // round trip time / 2
        public long ClockOffsetMs {get; private set;}// localTime + offset = peerTime
        public P2pNetPeer(string _p2pId)
        {
            p2pId = _p2pId;
        }

        // Ping/Timeout
        public long LastHeardFromTs {get; protected set; } // time when last heard from. 0 for never heard from
        public long LastSentToTs {get; protected set; } // stamp for last message we sent (use to throttle pings somewhat)

        public void UpdateLastHeardFrom() =>  LastHeardFromTs = P2pNetDateTime.NowMs;
        public void UpdateLastSentTo() =>  LastSentToTs = P2pNetDateTime.NowMs;

        // Clock sync
        public const int kNeverSynced = -1;
        public const int kSyncInProgress = -2;
        public long MsSinceClockSync { get => lastClockSyncMs == 0 ? kNeverSynced : (CurrentlySyncing() ? kSyncInProgress :   P2pNetDateTime.NowMs - lastClockSyncMs); }

        public void ReportSyncProgress() { lastClockSyncMs = -P2pNetDateTime.NowMs;} // negative means in progress
        public bool CurrentlySyncing() => lastClockSyncMs < 0;
        public bool ClockNeedsSync(int syncTimeoutMs)
        {
            // either not set or too long ago and not currently in progress
            return lastClockSyncMs == 0
                || (lastClockSyncMs > 0 && P2pNetDateTime.NowMs-lastClockSyncMs > syncTimeoutMs);
        }

        public void UpdateClockSync(long t0, long t1, long t2, long t3)
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
