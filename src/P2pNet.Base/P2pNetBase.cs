using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UniLog;
using static UniLog.UniLogger; // for SID()

namespace P2pNet
{
    // ReSharper disable InconsistentNaming
    // Problem here is that "p2p" is a word: "peer-to-peer" and the default .NET ReSharper rules dealing with digits result
    // in dumb stuff, like a field called "_p2PFooBar" with the 2nd P capped.


    public class P2pNetBase : IP2pNet, IP2pNetBase
    {
        public string LocalId  {get; private set; }
        public string LocalAddress {get; private set; }
        protected IP2pNetClient client;
        protected IP2pNetCarrier carrier;
        protected P2pNetChannelPeerPairings channelPeers;
        protected Dictionary<string, long> lastMsgIdSent; // last Id sent to each channel. Msg IDs are serial, and PER CHANNEL
        public UniLogger logger;

        // P2pNet Id is NOT necessarily the underlying network address (or id) of the peer.
        // It _is_, however, the pubsub channel for direct messages.
        public P2pNetBase(IP2pNetClient _client, IP2pNetCarrier _carrier, string localAddress, string localP2pNetId = null)
        {
            client = _client;
            carrier = _carrier;
            logger = UniLogger.GetLogger("P2pNet");
            LocalAddress = localAddress;
            LocalId = localP2pNetId ?? NewP2pId();
            ResetJoinStateVars();
        }

        // IP2pNet

        public P2pNetChannel GetNetworkChannel() =>channelPeers.MainChannel;

        private void ResetJoinStateVars()
        {
            channelPeers = new P2pNetChannelPeerPairings();
            lastMsgIdSent = new Dictionary<string, long>();
        }

        public void Join(P2pNetChannelInfo mainChannelInfo, string localHelloData)
        {
            // The localId "channel" is special - it's not really a P2pNet channel
            // at all: it's just where direct mssages come in, and there's not tracking
            // or timing or anything connected to it. On the other hand - if a message comes in
            // on it from a peer that is already in the channelPeers list (for a "real" channel)
            // then that peer it will get its "heardFrom" property updated.
            ResetJoinStateVars();
            carrier.Join(mainChannelInfo, this, localHelloData); // connects to network and listens on localId
        }

        public void OnNetworkJoined(P2pNetChannelInfo mainChannelInfo, string localHelloData)
        {
            // called back from _Join() when it is done - *** which might be async
            AddChannel(mainChannelInfo, localHelloData ); // Set up channel AND listen
            channelPeers.SetMainChannel( channelPeers.GetChannel(mainChannelInfo.id));
            client.OnPeerJoined( mainChannelInfo.id, LocalAddress, localHelloData);
        }

        public void Leave()
        {
            if (channelPeers.MainChannel != null)
                SendBye(channelPeers.MainChannel.Id); // might not have been set up yet
            carrier.Leave();
            ResetJoinStateVars(); // resets
        }

        public List<string> GetPeerAddrs() => channelPeers.GetPeerAddrs();
        public string GetPeerData(string channelId, string peerAddr) => channelPeers.GetChannelPeerByAddress(channelId, peerAddr)?.helloData;

        public PeerNetworkStats GetPeerNetworkStats(string peerAddr)
        {
            return PeerNetworkStats.CurrentNetworkStats(channelPeers.GetPeerByAddress(peerAddr), GetNetworkChannel().Id);
        }

        public void Update()
        {
            if (LocalId == null)
                return; // Not connected so don't bother

            //logger.Debug($"Update()");  Too much log.

            carrier.Poll(); // Do any network polling

            // TODO: iterating over everything this way is kinda brutish.
            // Ought to be able to figure out when things will need to get done in advance and
            // put them in a priority queue. Then in Update() just check which need to happen this "fame"
            // and do 'em.

            // During this update we are looking for:
            //
            // - Is this a chp we have never heard from at all? (special case - see AddPeer() )
            //        Should we say hello? SHould we give up on it?
            //
            // - Has this chp timed out?
            //        report it to the client? delete it from the P2pNet lists?
            //        No. Don't delete it from here. Should consider marking it "missing", though.
            //
            // - Do we need send a ping to this chp?
            //      Have we sent something to this chp recently? (do defer the ping)
            //

            // Keep in mind: under the current thinking a message or ping from a peer on one channel
            // DOES count towards "aliveness" on a different channel

            // Hello timeouts
            // We are in a "hello" exchange with a new (to us) peer - has it timed out?
            List<P2pNetChannelPeer> chpsThatFailedHello = channelPeers.ChannelPeers.Values
                .Where( chp => chp.HelloTimedOut()).ToList();
            foreach (P2pNetChannelPeer chp in chpsThatFailedHello)
            {
                logger.Warn($"Update - Failed HelloTimedOut(). Chp: {SID(chp.P2pId)}/{chp.ChannelId}");
                channelPeers.RemoveChannelPeer(chp); // Just drop it
            }

            // Regular timeouts involving known peers

            // Not long enough to be dropped - but long enough the app ought to know.
            // "Newly" means notification has not been sent to the client
            List<P2pNetChannelPeer> chpsThatAreNewlyMissing = channelPeers.ChannelPeers.Values
                .Where( chp => chp.IsNewlyMissing() ).ToList();

            foreach (P2pNetChannelPeer chp in chpsThatAreNewlyMissing)
            {
                logger.Warn($"Update - ChannelPeer {SID(chp.P2pId)}/{chp.ChannelId} is missing. Notifying client.");
                client.OnPeerMissing(chp.ChannelId, chp.P2pAddr); // on a client thread since called from poll()
                chp.MissingNotificationSent = true; // TODO: find a better way to keep from repeating these messages?
            }

            // Really, really gone. Report 'em and remove 'em
            List<P2pNetChannelPeer> chpsThatHaveTimedOut = channelPeers.ChannelPeers.Values
                .Where( chp => chp.HasTimedOut()).ToList();
            foreach (P2pNetChannelPeer chp in chpsThatHaveTimedOut)
            {
                logger.Warn($"Update - ChannelPeer {SID(chp.P2pId)}/{chp.ChannelId} timed out. Notifying client and removing peer.");
                client.OnPeerLeft( chp.ChannelId, chp.P2pAddr); // client thread
                channelPeers.RemoveChannelPeer(chp);
            }

            //
            // Who needs a ping?
            //
            // Go through all of the chp's and find all for which (now-lastSentTo <= channel.pingMs)

            List<P2pNetChannelPeer> chpsThatNeedPing = channelPeers.ChannelPeers.Values
                .Where( chp => chp.NeedsPing() ).ToList();

            // filter out chps that we think have dropped (Maybe combine all of this once we've got it down?)
            chpsThatNeedPing = chpsThatNeedPing.Where(chp => !chpsThatHaveTimedOut.Contains(chp)).ToList();  // TODO: Slow?

            // What are the channels? How many peers in each?

            // We want a list of (channelId, (peerList)) tuples sorted
            // by peerCount descending...
            List<(string,List<string>)> channelsWithPeers = chpsThatNeedPing.Select(chp => chp.ChannelId).Distinct() // unique channelIds
                    .Select(chId => (chId, chpsThatNeedPing.Where( chp => chp.ChannelId == chId ).Select(chp => chp.P2pId).ToList())) // (chId, List<peerId>),...
                    .OrderByDescending( tup => tup.Item2.Count)                                                   // sorted descendng be peerCnt
                    .ToList();

            // Now, we want that same list - but want for each peerId to only appear once: with the first channel it's listed with.
            // Idea is to group as many peers as possible.
            List<(string,List<string>)> filteredTuples = new List<(string,List<string>)>();
            List<string> usedPeerIds = new List<string>();
            foreach ( (string chId, List<string> peerIds) in channelsWithPeers)
            {
                List<string> remainingPeerIds = peerIds.Where(pid => !usedPeerIds.Contains(pid)).ToList();
                if (remainingPeerIds.Count > 0)
                {
                    filteredTuples.Add((chId, remainingPeerIds));
                    usedPeerIds.AddRange(peerIds);
                }
            }

            // OK - now for every channel with more than one peer brodcast a ping. For channels with a single peer send directly
            // TODO: maybe should need 3 recipients to be worth a broadcast ping?
            foreach ( (string chId, List<string> peerIds) in filteredTuples)
            {
                if (peerIds.Count > 1)
                    SendPing(chId); // broadcast
                else
                    SendPing(peerIds[0]);
            }

            // After all that... how about clock sync?
            List<P2pNetPeer> peersThatNeedSync = channelPeers.ChannelPeers.Values
                .Where( chp => chp.ClockNeedsSync()).Select(chp => chp.Peer).Distinct().ToList();

            foreach (P2pNetPeer peer in peersThatNeedSync)
                SendSync(peer.p2pId);

        }

        public void Send(string dest, string payload)
        {
            // dest can be peer address or channel id (peerId works, too, since it's a channel id)
            if (dest == LocalAddress)
            {
                client.OnClientMsg(LocalAddress, LocalAddress, 0, payload); // direct loopback
            } else {
                if (channelPeers.IsMainChannel(dest) || channelPeers.IsKnownChannel(dest))  // there are no P2pNetChannels for peerIds)
                    client.OnClientMsg(LocalAddress, dest, 0, payload); // broadcast channnel loopback

                // need to get p2pId for peer with dest Address
                P2pNetPeer destPeer = channelPeers.GetPeerByAddress(dest);

                string trueDest  =  destPeer == null ? dest : destPeer.p2pId;

                logger.Debug($"Send() Sending appMsg to {(channelPeers.IsMainChannel(dest) ? "main channel" : trueDest)}"); // TODO: make better

                DoSend(trueDest, P2pNetMessage.MsgAppl, payload);
            }
        }

        //public void AddPeer(string peerId) {} // really only makes sense for direct-connection transports

        // public void RemovePeer(string peerId)
        // {
        //     logger.Info($"RemovePeer() Removing: {SID(peerId)}");
        //     channelPeers.RemovePeer(peerId);
        // }

        public void AddSubchannel(P2pNetChannelInfo chan, string localHelloData) => AddChannel(chan, localHelloData);

        protected void AddChannel(P2pNetChannelInfo chanInfo, string localHelloData)
        {
            if (channelPeers.AddChannel(chanInfo, localHelloData))
            {
                P2pNetChannel chan = channelPeers.GetChannel(chanInfo.id);
                logger.Info($"Listening to channel: {chanInfo.id}");
                carrier.Listen(chan.Id);
                if (chan.Info.pingMs > 0)
                    SendHelloMsg(chan.Id, chan.Id ); // broadcast
            }
        }


        public void RemoveSubchannel(string chanId)
        {
            SendBye(chanId);
            channelPeers.RemoveChannel(chanId);
            carrier.StopListening(chanId);
        }


        // Transport-independent tasks

        protected string NewP2pId()
        {
            return Guid.NewGuid().ToString("N");
        }

        protected void DoSend(string dstChan, string msgType, string payload)
        {
            // Send() is the API for client messages
            long msgId = NextMsgId(dstChan);
            P2pNetMessage p2pMsg = new P2pNetMessage(dstChan, LocalId, msgId, msgType, payload);
            p2pMsg.sentTime = P2pNetDateTime.NowMs; // should not happen in ctor
            carrier.Send(p2pMsg);
            UpdateSendStats(dstChan, msgId);
        }

        public void OnReceivedNetMessage(string msgChannel, P2pNetMessage msg)
        {
            // NOTE: msgChannel is the channel on which the message was received,
            // which is by definition the same as msg.dstChannel
            // TODO: is the msgChannel param redundant and potentially confusing then?

            // Local messages have already been processed
            if (msg.srcId == LocalId)
                return; // main channel messages from local peer will show up here

            logger.Verbose($"OnReceivedNetMessage(): channel: {msgChannel}, MsgType: {msg.msgType} SrcId: {msg.srcId}, MsgId: {msg.msgId}");

            // If the peer was missing on any channels, inform those channels that it's back BEFORE handling the message
            foreach( P2pNetChannelPeer chp in channelPeers.ChannelPeersForPeer(msg.srcId) )
            {
                if (chp.IsMissing()) // Won't be missing anymore after UnpdateLastHeardFrom() is called for the peer
                    client.OnPeerReturned(chp.ChannelId, chp.P2pAddr);
                chp.MissingNotificationSent = false; // TODO: find a better way to handle only sending a missing notification once? Maybe?

            }
            channelPeers.GetPeerById(msg.srcId)?.UpdateLastHeardFrom(); // Don't need to do this in each handler

            P2pNetChannelPeer cp = channelPeers.GetChannelPeer(msgChannel, msg.srcId);
            if (cp?.ValidateMsgId(msg.msgId) == false)
            {
                logger.Warn($"_OnReceivedNetMessage(): Msg id #{msg.msgId} too early. Expecting #{cp.lastMsgId}");
            }


            // TODO: get rid of switch
            switch(msg.msgType)
            {
                case P2pNetMessage.MsgAppl:
                    OnAppMsg(msgChannel, msg);
                    break;
                case P2pNetMessage.MsgHello:
                    OnHelloMsg(msgChannel, msg);
                    break;
                case P2pNetMessage.MsgHelloReply:
                    OnHelloReplyMsg(msgChannel, msg);
                    break;
                case P2pNetMessage.MsgHelloBadChannelInfo:
                    OnHelloBadInfoMsg(msgChannel, msg);
                    break;
                case P2pNetMessage.MsgHelloAddressExists:
                    OnHelloAddressExistsMsg(msgChannel, msg);
                    break;
                case P2pNetMessage.MsgHelloChannelFull:
                    OnHelloChannelFullMsg(msgChannel, msg);
                    break;
                case P2pNetMessage.MsgGoodbye:
                    OnByeMsg(msgChannel, msg);
                    break;
                case P2pNetMessage.MsgPing:
                    OnPingMsg(msgChannel, msg);
                    break;
                case P2pNetMessage.MsgSync:
                    OnSyncMsg(msg.srcId, msg);
                    break;
            }
        }

        protected void OnAppMsg(string msgChanId, P2pNetMessage msg)
        {
            // dispatch a received client-app-specific message
            // Note that this is the only kind of message that gets fielded for non-tracking channels
            P2pNetPeer peer = channelPeers.GetPeerById(msg.srcId);

            P2pNetChannel channel = channelPeers.GetChannel(msg.dstChannel); // same as msgChanId
            if (channel == null && msgChanId != LocalId)
            {
                // I dunno if this can even happen.
                logger.Warn($"_OnAppMsg - Unknown channel: {msg.dstChannel}");
                return;
            }

            if (peer == null && channel?.Info.pingMs == 0) // unknown peer, non-tracking channel
            {
                // Add the channelPeer pair - the channel is non-tracking so there won;t be pings and it might time out
                logger.Debug($"_OnAppMsg - Adding unknown peer {SID(msg.srcId) } sending for non-tracking channel {channel.Id}");
                channelPeers.AddChannelPeer(channel.Id, msg.srcId); // add the channel/peer pair
                peer = channelPeers.GetPeerById(msg.srcId);
            }

            if (peer != null)
            {
                // If there's clock sync data for the channel then figure out when the msg was sent
                long realMsSinceSend = -1; // means no clock sync
                if (channel != null && channel.IsSyncingClocks)
                {
                    long remoteMsNow = P2pNetDateTime.NowMs + peer.ClockSyncInfo.sysClockOffsetMs; // TODO: too much work?
                    realMsSinceSend =  remoteMsNow - msg.sentTime;
                    if (realMsSinceSend < 0)
                    {
                        logger.Debug($"_OnAppMsg() msg from { SID(msg.srcId)} w/lag < 0: {realMsSinceSend}");
                        realMsSinceSend = 0;
                    }
                }


                logger.Debug($"_OnAppMsg - msg from ID: {SID(msg.srcId)} as address: {peer.p2pAddress}" );
                client.OnClientMsg(peer.p2pAddress, msg.dstChannel, realMsSinceSend, msg.payload);

            } else {
                logger.Warn($"_OnAppMsg - Unknown peer {SID(msg.srcId)} sending on channel {msgChanId} for channel {msg.dstChannel}");
            }
        }

        protected long NextMsgId(string chan)
        {
            try {
                return lastMsgIdSent[chan] + 1;
            } catch (KeyNotFoundException){
                return 1;
            }
        }

        protected void UpdateSendStats(string chanId, long latestMsgId)
        {
            // Figure out who is actually getting this message and update the "last time we sent anything to them" property.
            // Note that this is per peer - sending to a peer on one channel DOES update if we think we need to ping on another channel.

            lastMsgIdSent[chanId] = latestMsgId; // note that this DOES include direct message "channels"

            P2pNetChannelInfo chanData = channelPeers.GetChannel(chanId)?.Info;

            if (chanData == null) // it was sent directly to a peer
            {
                channelPeers.GetPeerById(chanId)?.UpdateLastSentTo();
            }
            else
            {
                if (chanData.pingMs > 0) // it's a tracking channel
                {
                    foreach (P2pNetPeer p in channelPeers.PeersForChannel(chanId))
                        p.UpdateLastSentTo(); // so we don't ping until it's needed
                }
            }
        }

        // Some specific messages

        public void SendHelloMsg(string destChannel, string subjectChannel, string helloMsgType = P2pNetMessage.MsgHello)
        {
            logger.Verbose($"SendHelloMsg(): Sending: {helloMsgType} to channel: {destChannel}");
            // When joining a new channel, destChannel and subjectChannel are typically the same.
            // When replying, or sending to a single peer, the destChannel is usually the recipient peer
            P2pNetChannel chan = channelPeers.GetChannel(subjectChannel);
            DoSend(destChannel, helloMsgType, JsonConvert.SerializeObject(new HelloPayload(LocalAddress, chan.Info, chan.LocalHelloData)));
        }

        protected bool _PeerAddrIsBad(string channelId , string senderId, string senderAddr)
        {
            // Is it OUR address!?!?!!
            if ((senderId != LocalId) && (senderAddr == LocalAddress))
            {
                // A msg from localId shouldn;t ever get here, but no harm in checking
                logger.Warn($"_PeerAddrIsBad() Peer Address: {SID(senderAddr)} OUR address!! Removing remote peer." );
                SendHelloMsg(senderId, channelId, P2pNetMessage.MsgHelloAddressExists);
                return true;
            }

            // Is it an existing peer (it probably is) with a different *non-null* address?
            string existingPeerAddr = channelPeers.GetPeerById(senderId)?.p2pAddress;
            if (!string.IsNullOrEmpty(existingPeerAddr) && existingPeerAddr!= senderAddr)
            {
                logger.Warn($"_PeerAddrIsBad() Peer ID: {SID(senderId)} alreadys exists with different address: {SID(senderAddr)}. Removing new peer." );
                SendHelloMsg(senderId, channelId, P2pNetMessage.MsgHelloAddressExists);
                return true;
            }

            // Is there and existing peer with this addres and a different ID?
            string existingPeerId = channelPeers.GetPeerByAddress(senderAddr)?.p2pId;
            if (!string.IsNullOrEmpty(existingPeerId) && existingPeerId!= senderId)
            {
                logger.Warn($"_PeerAddrIsBad() Peer Address: {SID(senderAddr)} is already in use for a different peer: {SID(senderId)} Removing new peer." );
                SendHelloMsg(senderId, channelId, P2pNetMessage.MsgHelloAddressExists);
                return true;
            }
            return false;
        }

        protected void OnHelloMsg(string unusedSrcChannel, P2pNetMessage msg)
        {
            HelloPayload hp = JsonConvert.DeserializeObject<HelloPayload>(msg.payload);
            string senderId = msg.srcId;
            string senderAddr = hp.peerAddress;
            P2pNetChannel channel = channelPeers.GetChannel(hp.channelInfo.id);

            logger.Verbose($"OnHelloMsg(): for channel: {channel.Id} From id: {senderId}  Address: {senderAddr}" );

            if ( !channel.Info.IsEquivalentTo(hp.channelInfo) )
            {
                logger.Warn($"OnHelloMsg - Bad channel info in HELLO for {channel.Id} from peer {SID(senderId)}, address: {SID(senderAddr)}" );
                SendHelloMsg(senderId, channel.Id, P2pNetMessage.MsgHelloBadChannelInfo);
                return;
            }

            // Is the channel full?
            if ( (channel.Info.maxPeers > 0) && (channelPeers.PeersForChannel(channel.Id).Count >= channel.Info.maxPeers) )
            {
                logger.Warn($"OnHelloMsg() Channel {channel.Id} is FULL. Refuse HELLO" );
                SendHelloMsg(senderId, channel.Id, P2pNetMessage.MsgHelloChannelFull);
                return;
            }

            if (_PeerAddrIsBad(channel.Id, senderId, senderAddr))
            {
                channelPeers.RemovePeer(senderId);
                return;
            }

            P2pNetChannelPeer chp = channelPeers.GetChannelPeer(channel.Id, senderId);
            if (chp == null)
                chp = channelPeers.AddChannelPeer(channel.Id, senderId);

            if (chp.helloData == null) // It's OK for a peer already in a channel to send hello again - but we'll ignore it
            {
                // new peer (to us)
                logger.Verbose($"_OnHelloMsg - Hello for channel {chp.ChannelId} from peer id: {SID(chp.P2pId)}, address: {SID(senderAddr)}" );
                chp.helloData = hp.peerChannelHelloData;
                channelPeers.UpdatePeerAddress(chp.Peer, senderAddr);
                chp.Peer.UpdateLastHeardFrom();
                logger.Verbose($"OnHelloMsg - replying directly to {SID(chp.P2pId)} about channel {chp.ChannelId}");
                SendHelloMsg(chp.P2pId, chp.ChannelId, P2pNetMessage.MsgHelloReply); // This is a reply

                logger.Verbose($"OnHelloMsg - calling client to report new peer.");
                client.OnPeerJoined(chp.ChannelId, chp.P2pAddr, hp.peerChannelHelloData);
            }
        }

        protected void OnHelloReplyMsg(string unusedSrcChannel, P2pNetMessage msg)
        {
            HelloPayload hp = JsonConvert.DeserializeObject<HelloPayload>(msg.payload);
            string senderId = msg.srcId;
            string senderAddr = hp.peerAddress;

            logger.Verbose($"OnHelloReplyMsg(): for channel: {hp.channelInfo.id} From id: {senderId} as address: {senderAddr}" );

            // This shouldn't be able to happen because sender of the reply would have sent an AddressIsBad reply.
            if (_PeerAddrIsBad(hp.channelInfo.id, senderId, senderAddr))
            {
                logger.Warn($"OnHelloReplyMsg(): Should not be able to get a HelloReply with a bad address." );
                channelPeers.RemovePeer(senderId);
                return;
            }

            P2pNetChannelPeer chp = channelPeers.GetChannelPeer(hp.channelInfo.id, senderId);
            if (chp == null)
                chp = channelPeers.AddChannelPeer(hp.channelInfo.id, senderId);

            if (chp.helloData == null)
            {
                logger.Verbose($"OnHelloReplyMsg - HelloReply for channel {chp.ChannelId} from new peer {SID(chp.P2pId)}, address: {SID(senderAddr)}" );
                chp.helloData = hp.peerChannelHelloData;
                channelPeers.UpdatePeerAddress(chp.Peer, senderAddr);
                chp.Peer.UpdateLastHeardFrom();
                logger.Verbose($"OnHelloReplyMsg - calling client.");
                client.OnPeerJoined(chp.ChannelId, chp.P2pAddr, hp.peerChannelHelloData);
            } else {
                // Peer already in place. Probably not a bad thing, but weird?
                logger.Warn($"Redundant OnHelloReplyMsg(): for channel: {hp.channelInfo.id} From id: {senderId} as address: {senderAddr}" );
            }
        }

        protected void OnHelloBadInfoMsg(string srcId, P2pNetMessage msg)
        {
            HelloPayload hp = JsonConvert.DeserializeObject<HelloPayload>(msg.payload);
            logger.Warn($"OnHelloBadInfoMsg() - Bad channel info reported for {hp.channelInfo.id} from peer id: {SID(msg.srcId)} Address: {hp.peerAddress}" );

            //if (channelPeers.RemoveChannelPeer(hp.channelInfo.id, srcId))  // THey won;t be talking to us anyway
            //    client.OnPeerLeft(hp.channelInfo.id, hp.peerAddress);

            // Assume that we are wrong since we sent a Hello broadcast and this is a reply from someone elready there
            RemoveSubchannel(hp.channelInfo.id);
            client.OnJoinRejected(hp.channelInfo.id, "Bad Channel Info");
        }

        protected void OnHelloAddressExistsMsg(string srcId, P2pNetMessage msg)
        {
            HelloPayload hp = JsonConvert.DeserializeObject<HelloPayload>(msg.payload);
            logger.Warn($"OnHelloBadAddressMsg() - Bad address (already exists w/different ID) reported for {hp.channelInfo.id} from peer id: {SID(msg.srcId)} Addr: {hp.peerAddress}" );
            // We MIGHT know this peer if we got their hello first. We should delete them since they clearly won;t be talking to us and will time out.
            //  if (channelPeers.RemoveChannelPeer(hp.channelInfo.id, srcId))
            //    client.OnPeerLeft(hp.channelInfo.id, hp.peerAddress);

            // Assume that we are wrong since we sent a Hello broadcast and this is a reply from someone elready there
            RemoveSubchannel(hp.channelInfo.id);
            client.OnJoinRejected(hp.channelInfo.id, "Address Already Joined");
        }

        protected void OnHelloChannelFullMsg(string srcId, P2pNetMessage msg)
        {
            HelloPayload hp = JsonConvert.DeserializeObject<HelloPayload>(msg.payload);
            logger.Warn($"OnHelloChannelFullMsg() - Channel full reported for {hp.channelInfo.id} from peer {SID(msg.srcId)}  Address: {hp.peerAddress}" );

            // Assume that we are wrong since we sent a Hello broadcast and this is a reply from someone elready there
            RemoveSubchannel(hp.channelInfo.id);
            client.OnJoinRejected(hp.channelInfo.id, "Channel Full");
        }

        protected void SendPing(string chanId)
        {
            // Read the calling code to see how whether and whom to ping works.
            string toWhom = channelPeers.IsMainChannel(chanId) ? "Everyone" : SID(chanId);
            logger.Verbose(string.Format($"_SendPing() - Sending to {toWhom} on ch: {chanId}" ));
            DoSend(chanId, P2pNetMessage.MsgPing, null);
        }

        protected void OnPingMsg(string srcChannel, P2pNetMessage msg)
        {
            logger.Verbose($"*{SID(LocalAddress)}: _OnPingMsg - Ping from ID {SID(msg.srcId)}"); // this is NOT the src address
            // Don't really do anything. We already called updateLastHeardFrom for the peer
        }

        protected void SendBye(string chanId)
        {
             logger.Verbose($"SendBye() channel: {chanId}");
            DoSend(chanId, P2pNetMessage.MsgGoodbye, null);
        }

        protected void OnByeMsg(string srcChannel, P2pNetMessage msg)
        {
            logger.Verbose($"OnByeMsg() channel: {srcChannel} PeerId: {SID(msg.srcId)}");
            channelPeers.RemoveChannelPeer(srcChannel, msg.srcId);
            P2pNetPeer peer = channelPeers.GetPeerById(msg.srcId);

            if (peer != null)
                client.OnPeerLeft(srcChannel, peer.p2pAddress);

            if (srcChannel == channelPeers.MainChannel.Id)
            {
                channelPeers.RemovePeer(msg.srcId);
            }
        }

        // NOTE: sync packets use the actual message timestamps. So, for insntance, when the
        // first Sync is sent t0 is NOT set - it gets set by the recipient from the
        // sentTime field. Liekwise, when it gets to _OnSynMsg, the receipt t1 or t3
        // is set from the incoming msg rcvdTime field.
        protected void SendSync(string destId, SyncPayload _payload=null)
        {
            SyncPayload payload = _payload ?? new SyncPayload();
            P2pNetPeer peer = channelPeers.GetPeerById(destId);
            if (peer != null)   // seen it happen
            {
                peer.ReportInterimSyncProgress();
                // payload "sent time" gets set by receiver.
                DoSend(destId, P2pNetMessage.MsgSync, JsonConvert.SerializeObject(payload));
                logger.Info($"Initiating sync with peer {SID(destId)}");
            }
        }

        private void _completeSyncForPeer( P2pNetPeer peer, long t0, long t1, long t2, long t3)
        {
            peer.CompleteClockSync(t0, t1, t2, t3);
            PeerClockSyncInfo csi = peer.ClockSyncInfo;
            logger.Info($"  _completeSyncForPeer(): {SID(peer.p2pId)} Offset: {csi.sysClockOffsetMs}, Lag: {csi.networkLagMs}");
            foreach (P2pNetChannel ch in channelPeers.ChannelsForPeer(peer.p2pId))
            {
                if (ch.IsSyncingClocks)
                    client.OnPeerSync(ch.Id,peer.p2pAddress, csi);
            }
        }

        protected void OnSyncMsg(string from, P2pNetMessage msg)
        {
             P2pNetPeer peer = channelPeers.GetPeerById(from);
            if (peer != null)
            {
                peer.ReportInterimSyncProgress();
                SyncPayload payload = JsonConvert.DeserializeObject<SyncPayload>(msg.payload);
                if (payload.t0 == 0)
                {
                    logger.Info($"OnSyncMsg() request from {SID(from)}");
                    // This was the first hop from the originator
                    payload.t0 = msg.sentTime;
                    payload.t1 = msg.rcptTime;
                    DoSend(from, P2pNetMessage.MsgSync, JsonConvert.SerializeObject(payload)); // send reply
                } else if (payload.t2 == 0) {
                    // We are the originator getting our sync back
                    logger.Info($"OnSyncMsg reply (our init) from {SID(from)}");
                    payload.t2 = msg.sentTime;
                    payload.t3 = msg.rcptTime;
                    DoSend(from, P2pNetMessage.MsgSync, JsonConvert.SerializeObject(payload)); // send reply
                    _completeSyncForPeer(peer, payload.t0, payload.t1, payload.t2, payload.t3 );

                    //peer.CompleteClockSync(payload.t0, payload.t1, payload.t2, payload.t3);
                    // PeerClockSyncInfo csi = peer.ClockSyncInfo;
                    // foreach (P2pNetChannel ch in channelPeers.ChannelsForPeer(peer.p2pId))
                    // {
                    //     if (ch.IsSyncingClocks)
                    //         client.OnPeerSync(ch.Id,peer.p2pId, csi);
                    //         // TODO: OnPeerSYnc should just take a PeerClockSYncInfo?
                    // }

               } else {
                    // we're the recipient and it's done
                    logger.Info($"Sync reply (remote init) from {SID(from)}");
                    _completeSyncForPeer(peer,payload.t2, payload.t3, msg.sentTime, msg.rcptTime );

                    // peer.CompleteClockSync(payload.t2, payload.t3, msg.sentTime, msg.rcptTime);
                    // PeerClockSyncInfo csi = peer.ClockSyncInfo;
                    // //logger.Info($"  Offset: {csi.sysClockOffsetMs}, Lag: {csi.networkLagMs}");
                    // // TODO: fix the following copypasta
                    // foreach (P2pNetChannel ch in channelPeers.ChannelsForPeer(peer.p2pId))
                    // {
                    //     if (ch.IsSyncingClocks)
                    //         client.OnPeerSync(ch.Id,peer.p2pId, csi);
                    // }
                }
            } else {
               logger.Warn($"Got sync from unknown peer: {SID(from)}. Ignoring.");
            }
        }

    }
}
