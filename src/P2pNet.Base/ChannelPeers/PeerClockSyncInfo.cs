namespace P2pNet
{

    public class PeerClockSyncInfo
    {
        public string peerId;
        public long networkLagMs; // round trip time / 2
        public long clockOffsetMs; // localTime + offset = remoteTime
        public long msSinceLastSync;
        public PeerClockSyncInfo(string pid, long since, long offset, long lag)
        {
            peerId = pid;
            msSinceLastSync = since;
            networkLagMs = lag;
            clockOffsetMs = offset;
        }
    }
}