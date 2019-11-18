using System.Security.Cryptography;
using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace P2pNet
{
    public interface IP2pNetClient
    {
        object P2pHelloData();
        void OnPeerJoined(string p2pId, object helloData);
        void OnPeerLeft(string p2pId);
        void OnP2pMsg(string from, string to, object msgData);
    }
    public interface IP2pNet
    {
        string GetId();
        string Join(string mainChannel); // returns local ID
        List<string> GetPeerIds();
        object GetPeerData(string peerId);
        void Leave();
        void Send(string chan, object msgData);
        void AddPeer(string peerId);
    }

    public class P2pNetPeer
    {
        public string p2pId;
        public object helloData;
        public long firstHelloSentTs = 0; // when did we FIRST send a hello/hello req? (for knowing when to give up)
        public bool helloReceived = false; // we have gotten this node's basic info (ie. it has joined the net)
        public long lastHeardTs = 0; // time when last heard from. 0 for never heard from
        public long lastSentToTs = 0; // stamp for last message we sent (use to throttle pings somewhat)
        public long lastMsgId = 0; // Last msg rcvd from this peer. Each peer tags each mesage with a serial # (nextMsgId in P2PNetBase)

        public bool ValidateMsgId(long msgId)
        {
            // reject any new msg w/ id <= what we have already seen
            if (msgId <= lastMsgId)
                return false;
            lastMsgId = msgId;
            return true;
        }
    }

    public class P2pNetMessage
    {
        // Note that a P2pNetClient never sees this
        // TODO: How to make this "internal" and still allow P2pNetBase._Send() to be protected

        public const string MsgHello = "HELLO";
        public const string MsgGoodbye = "BYE";
        public const string MsgPing = "PING";
        public const string MsgAppl = "APPMSG";

        public string dstChannel;
        public string srcId;
        public long msgId;
        public string msgType;
        public string msgJson; // json-encoded message object

        public P2pNetMessage(string _dstChan, string _srcId, long _msgId, string _msgType, object _msgData)
        {
            dstChannel = _dstChan;
            srcId = _srcId;
            msgId = _msgId;
            msgType = _msgType;
            msgJson = JsonConvert.SerializeObject(_msgData);
        }
    }

    public abstract class P2pNetBase : IP2pNet
    {
        protected string localId;
        protected string mainChannel; // broadcasts go here
        protected IP2pNetClient client;
        protected string connectionStr; // Transport-dependent format
        protected Dictionary<string, P2pNetPeer> peers;
        protected Dictionary<string, long> lastMsgIdSent; // last Id sent to each channel/peer

        public long nowMs => DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

        public P2pNetBase(IP2pNetClient _client, string _connectionStr)
        {
            client = _client;
            connectionStr = _connectionStr;
            _InitJoinParams();
        }

        // IP2pNet
        public string GetId() => localId;
        public string Join(string _mainChannel)
        {
            _InitJoinParams();
            mainChannel = _mainChannel;
            localId = _Join(mainChannel);
            _SendHello(mainChannel);
            return localId;
        }

        public List<string> GetPeerIds() => peers.Keys.ToList();

        public object GetPeerData(string peerId)
        {
            try {
                return peers[peerId].helloData;
            } catch(KeyNotFoundException) {
                return null;
            }
        }

        public void Send(string chan, object applMsgData)
        {
            if (chan == localId)
                client.OnP2pMsg(localId, localId, applMsgData); // loopback
            else
                _DoSend(chan, P2pNetMessage.MsgAppl, applMsgData);
        }

        public void AddPeer(string peerId) {} // really only makes sense for direct-connection transports
        public void Leave()
        {
            _SendBye();
            _Leave();
            _InitJoinParams();
        }

        // Implementation methods
        protected abstract string _Join(string mainChannel);
        protected abstract bool _Send(P2pNetMessage msg);
        protected abstract void _Listen(string channel);
        protected abstract void _StopListening(string channel);
        protected abstract void _Leave();

        // Transport-independent tasks

        protected void _DoSend(string dstChan, string msgType, object msgData)
        {
            // Send() is the API for client messages
            long msgId = _NextMsgId(dstChan);
            P2pNetMessage p2pMsg = new P2pNetMessage(dstChan, localId, msgId, msgType, msgData);
            if (_Send(p2pMsg))
                _UpdateSendStats(dstChan, msgId);
        }


        protected void _OnReceivedNetMessage(string srcChannel, P2pNetMessage msg)
        {
            // TODO: update receipt stats
            // TODO: get rid of switch
            switch(msg.msgType)
            {
                case P2pNetMessage.MsgAppl:
                    _OnAppMsg(srcChannel, msg);
                    break;
                case P2pNetMessage.MsgHello:
                    _OnHelloMsg(srcChannel, msg);
                    break;
                case P2pNetMessage.MsgGoodbye:
                    _OnByeMsg(srcChannel, msg);
                    break;
            }
        }

        protected void _OnAppMsg(string srcChannel, P2pNetMessage msg)
        {
            // dispatch a received client message
            object msgData = JsonConvert.DeserializeObject(msg.msgJson);
            client.OnP2pMsg(msg.srcId, msg.dstChannel, msgData);
        }


        protected void _InitJoinParams()
        {
            peers = new Dictionary<string, P2pNetPeer>();
            lastMsgIdSent = new Dictionary<string, long>();
            localId = null;
        }

        protected long _NextMsgId(string chan)
        {
            try {
                return lastMsgIdSent[chan] + 1;
            } catch (KeyNotFoundException){
                return 1;
            }
        }

        protected void _UpdateSendStats(string channel, long latestMsgId)
        {
            lastMsgIdSent[channel] = latestMsgId;

            if (channel == mainChannel)
                foreach (P2pNetPeer p in peers.Values)
                    p.lastSentToTs = nowMs;
            else
                peers[channel].lastSentToTs = nowMs;
        }

        // Some specific messages

        protected void _SendHello(string channel)
        {
            object helloData = client.P2pHelloData();
            _DoSend(channel, P2pNetMessage.MsgHello, helloData);
        }

        protected void _OnHelloMsg(string srcChannel, P2pNetMessage msg)
        {
            if (!peers.ContainsKey(msg.srcId))
            {
                P2pNetPeer p = new P2pNetPeer();
                object helloData = JsonConvert.DeserializeObject(msg.msgJson);
                p.p2pId = msg.srcId;
                p.helloReceived = true;
                p.helloData = helloData;
                peers[p.p2pId] = p;
                _SendHello(p.p2pId);
                client.OnPeerJoined(msg.srcId, helloData);
            }
        }

        protected void _SendBye()
        {
            object helloData = client.P2pHelloData();
            _DoSend(mainChannel, P2pNetMessage.MsgGoodbye, null);
        }

        protected void _OnByeMsg(string srcChannel, P2pNetMessage msg)
        {
           client.OnPeerLeft(msg.srcId);
            _StopListening(msg.srcId);
            peers.Remove(msg.srcId);
        }

    }

}
