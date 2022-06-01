using System;
using UniLog;

namespace P2pNet
{

    public class P2pNetChannelPeer
    {
        // This is a Channel/Peer pairing.
        // P2pNet in most ways deals with channels as individual "networks" for things like
        // membership tracking, and so things like dscovery (helloData) and drop timing (and pings) are
        // done per "ChannelPeer" rather than per peer.
        // So state data that might otherwise be expected to be per-peer lives here instead.
        //
        // Note that this is NOT the case for the NTP-style network timing estimates. They are really per-peer.

        public P2pNetChannel Channel { get; private set; }
        public P2pNetPeer Peer { get; private set; }
        public string helloData;
        protected long firstHelloSentTs; // default: 0 - when did we FIRST send a hello/hello req? (for knowing when to give up)
        // TODO: need to set the above either in the constructor (if it includes hello data)
        // or when we send a hello to a peer that has firstHelloSentTs == 0;
        public long lastMsgId; // default 0 -  Last msg rcvd from this channelPeer. Send tags each message with a serial # (nextMsgId in P2PNetBase)

        public P2pNetChannelPeer(P2pNetPeer peer, P2pNetChannel channel)
        {
            Channel = channel;
            Peer = peer;
        }

        public string P2pId { get => Peer.p2pId;}
        public string ChannelId { get => Channel.Id;}

        public bool HaveTriedToContact() => firstHelloSentTs > 0;

        public bool HaveHeardFrom() => helloData != null;

        public bool WeShouldSendHello()
        {
            // Should we send hello to a node we've never heard from?
            // Yes if:
            // - We have never gotten a hello from them  AND
            //      - If we've never sent a hello.
            //      OR
            //      - it's been more that a ping-time since we did. (retry)
            return Channel.IsTrackingMemberShip
                && !HaveHeardFrom()
                && ( !HaveTriedToContact()
                  || (P2pNetDateTime.NowMs - Peer.LastHeardFromTs > Channel.Info.pingMs)
                );
        }

        public bool HelloTimedOut()
        {
            return !HaveHeardFrom()
                && HaveTriedToContact()
                && (P2pNetDateTime.NowMs - firstHelloSentTs > Channel.Info.dropMs);
        }

        public bool IsMissing() // Keep in mind: "missing" is per-channel/peer
        {
            return HaveHeardFrom()
                && Channel.IsTrackingMemberShip  // must be tracking/pinging
                && Channel.ReportsMissingPeers
                && P2pNetDateTime.NowMs - Peer.LastHeardFromTs > Channel.Info.missingMs;
        }

        public bool MissingNotificationSent {get; set;} // Notification sent to local app. So we don't send another "missing" notification

        public bool IsNewlyMissing()
        {
            return IsMissing() && !MissingNotificationSent;
        }

        public bool HasTimedOut()
        {
            return HaveHeardFrom()
                &&  Channel.Info.dropMs > 0
                && P2pNetDateTime.NowMs - Peer.LastHeardFromTs > Channel.Info.dropMs;
        }

        public void UpdateLastSentTo() =>  Peer.UpdateLastSentTo();

        public bool NeedsPing() => (Channel.Info.pingMs > 0) && (P2pNetDateTime.NowMs - Peer.LastSentToTs) > Channel.Info.pingMs;

        public bool ClockNeedsSync() => (Channel.Info.netSyncMs> 0 ) && Peer.ClockNeedsSync(Channel.Info.netSyncMs);

        public bool ValidateMsgId(long msgId)
        {
            // fail any new msg w/ id <= what we have already seen
            if (msgId <= lastMsgId)
                return false;
            lastMsgId = msgId;
            return true;
        }

    }

}
