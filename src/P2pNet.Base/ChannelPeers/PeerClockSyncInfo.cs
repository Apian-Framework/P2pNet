namespace P2pNet
{

    public class PeerClockSyncInfo
    {
        public string peerId;
        public long networkLagMs; // round trip time / 2
        public double networkLagSigma; // Std deviation
        public long sysClockOffsetMs; // localTime + offset = remoteTime
        public double sysClockOffsetSigma;
        public long msSinceLastSync;
        public long syncCount; // number of synd test samples
        public PeerClockSyncInfo(string pid, long cnt, long since, long offset, double offsetSigma, long lag, double lagSigma)
        {
            peerId = pid;
            syncCount = cnt;
            msSinceLastSync = since;
            networkLagMs = lag;
            networkLagSigma = lagSigma;
            sysClockOffsetMs = offset;
            sysClockOffsetSigma = offsetSigma;
        }
    }
}