namespace P2pNet
{
    public class P2pNetMessage
    {
        // Note that a P2pNetClient never sees this
        // TODO: How to make this "internal" and still allow P2pNetBase._Send() to be protected
        public const string MsgHello = "HELLO"; // recipient should reply
        public const string MsgHelloReply = "HRPLY"; // do not reply to this
        public const string MsgHelloBadChannelInfo = "BADINF"; // On MsgHello with bad channel info send this as a reply (don't add peer)
        public const string MsgHelloChannelFull = "CHFULL"; // On MsgHello in a full channel send this as a reply (don't add peer)
        public const string MsgGoodbye = "BYE";
        public const string MsgPing = "PING";
        public const string MsgAppl = "APPMSG";
        public const string MsgSync = "SYNC"; // clock sync


        public string dstChannel;
        public string srcId;
        public long msgId;
        public long sentTime; // millisecs timestamp at sender
        public long rcptTime; // millisecs timestamp at recipient (here)
        public string msgType;
        public string payload; // string or json-encoded application object

        public P2pNetMessage(string _dstChan, string _srcId, long _msgId, string _msgType, string _payload)
        {
            dstChannel = _dstChan;
            srcId = _srcId;
            msgId = _msgId;
            msgType = _msgType;
            payload = _payload;
            sentTime = -1; // gets set on send
            rcptTime = -1; // gets set on receipt
        }
    }

    // TODO: rename these or maybe make P2pNetMessage internal classes?
    public class HelloPayload
    {
        public P2pNetChannelInfo channelInfo;
        public string peerChannelHelloData;
        public HelloPayload(P2pNetChannelInfo chInfo, string helloData) {channelInfo = chInfo; peerChannelHelloData = helloData;}
    }

    public class SyncPayload
    {
        public long t0; //  t0 to originator
        public long t1; // t1 for org
        public long t2; //t2 for org, t0 for recip
        public long t3; // t3 for org, t1 for recip
        public long t4; // t2 for recip - t2 for it doesn't need to be in payload
        public SyncPayload() {t0=0; t1=0; t2=0; t3=0;}
        public override string ToString() => $"{{t0:{t0} t1:{t1} t2:{t2} t3:{t3}}}";
    }
}