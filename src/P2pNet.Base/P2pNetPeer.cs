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
        public string helloData;
        protected long firstHelloSentTs = 0; // when did we FIRST send a hello/hello req? (for knowing when to give up)
        // TODO: need to set the above either in the constructor (if it includes hello data)
        // or when we send a hello to a peer that has firstHelloSentTs == 0;
        protected long lastHeardTs = 0; // time when last heard from. 0 for never heard from
        protected long lastSentToTs = 0; // stamp for last message we sent (use to throttle pings somewhat)
        protected long lastClockSyncMs = 0; // so we know if we're syncing
        protected long lastMsgId = 0; // Last msg rcvd from this peer. Each peer tags each mesage with a serial # (nextMsgId in P2PNetBase)
        protected int pingTimeoutMs;
        protected int dropTimeoutMs;
        protected int syncTimeoutMs;

        public  long NetworkLagMs {get; private set;} = 0; // round trip time / 2
        public long ClockOffsetMs {get; private set;} = 0; // localTime + offset = peerTime
        public long MsSinceClockSync { get => lastClockSyncMs == 0 ? -1 : (CurrentlySyncing() ? syncTimeoutMs :   P2pNetDateTime.NowMs - lastClockSyncMs); } // -1 if never synced

        public P2pNetPeer(string _p2pId, int _pingMs, int _dropMs, int _syncMs)
        {
            p2pId = _p2pId;
            pingTimeoutMs = _pingMs;
            dropTimeoutMs = _dropMs;
            syncTimeoutMs = _syncMs;
        }

        // Clock sync
        public void ReportSyncProgress() { lastClockSyncMs = -P2pNetDateTime.NowMs;} // negative means in progress
        public bool CurrentlySyncing() => lastClockSyncMs < 0;
        public bool ClockNeedsSync()
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

        public bool HaveTriedToContact() => firstHelloSentTs > 0;

        public bool HaveHeardFrom() => helloData != null;

        public bool WeShouldSendHello()
        {
            // Should we send hello to a node we've never heard from?
            // Yes if:
            // - If we've never sent a hello.
            // - it's been more that a ping-time since we did.
            if (HaveHeardFrom())
                return false;
            if (!HaveTriedToContact())
                return true;
            return (P2pNetDateTime.NowMs - lastSentToTs) > pingTimeoutMs;
        }

        public bool HelloTimedOut()
        {
            long elapsed = P2pNetDateTime.NowMs - lastHeardTs;
            //if ( elapsed > _pingTimeoutSecs * 3 / 2)
            //    logger.Info($"Peer is late: {p.p2pID}");
            // TODO: worth pinging?
            return (elapsed > dropTimeoutMs);
        }

        public bool HasTimedOut()
        {
            // Not hearing from them?
            long elapsed = P2pNetDateTime.NowMs - lastHeardTs;
            return (elapsed > dropTimeoutMs);
        }

        public void UpdateLastHeardFrom() =>  lastHeardTs = P2pNetDateTime.NowMs;

        public void UpdateLastSentTo() =>  lastSentToTs = P2pNetDateTime.NowMs;

        public bool NeedsPing() => (P2pNetDateTime.NowMs - lastSentToTs) > pingTimeoutMs;

        public bool ValidateMsgId(long msgId)
        {
            // reject any new msg w/ id <= what we have already seen
            if (msgId <= lastMsgId)
                return false;
            lastMsgId = msgId;
            return true;
        }
    }

}
