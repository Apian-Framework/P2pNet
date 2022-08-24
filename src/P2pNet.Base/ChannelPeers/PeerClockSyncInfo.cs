namespace P2pNet
{

    public class PeerClockSyncInfo
    {
        public string peerId;
        public int networkLagMs; // round trip time / 2
        public double networkLagSigma; // Std deviation
        public int sysClockOffsetMs; // localTime + offset = remoteTime
        public double sysClockOffsetSigma;
        public int msSinceLastSync;
        public long syncCount; // number of synd test samples
        public PeerClockSyncInfo(string pid, long cnt,int since, int offset, double offsetSigma, int lag, double lagSigma)
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