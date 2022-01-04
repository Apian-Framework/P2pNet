namespace P2pNet
{

    public class P2pNetClockSyncData
    {
        // ReSharper disable MemberCanBePrivate.Global
        public string peerId;
        public long networkLagMs; // round trip time / 2
        public long clockOffsetMs; // localTime + offset = peerTime
        public long msSinceLastSync;
        public P2pNetClockSyncData(string pid, long since, long offset, long lag)
        {
            peerId = pid;
            msSinceLastSync = since;
            networkLagMs = lag;
            clockOffsetMs = offset;
        }
    }
}