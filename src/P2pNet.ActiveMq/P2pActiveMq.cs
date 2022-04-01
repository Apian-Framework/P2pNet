﻿using System;
using System.Collections.Generic;
using Apache.NMS;
using Apache.NMS.ActiveMQ;
using Newtonsoft.Json;

namespace P2pNet
{

    public class P2pActiveMq : P2pNetBase
    {
        private readonly IConnection connection;
        private ISession session;
        private readonly Dictionary<string, MessageListener> listeningDict;
        private List<P2pNetMessage> messageQueue;
        private readonly object queueLock = new object();

        public P2pActiveMq(IP2pNetClient _client, string _connectionString) : base(_client, _connectionString)
        {
            messageQueue = new List<P2pNetMessage>();
            listeningDict = new Dictionary<string, MessageListener>();

            // Example: "username,password,activemq:tcp://hostname:61616";
            string[] parts = _connectionString.Split(new string[]{","},StringSplitOptions.None);
            IConnectionFactory factory = new ConnectionFactory(parts[2]);
            connection = factory.CreateConnection(parts[0], parts[1]);
        }

        protected override void CarrierProtocolPoll()
        {
            if (messageQueue.Count > 0)
            {
                List<P2pNetMessage> prevMessageQueue;
                lock(queueLock)
                {
                    prevMessageQueue = messageQueue;
                    messageQueue = new List<P2pNetMessage>();
                }

                foreach( P2pNetMessage msg in prevMessageQueue)
                {
                    OnReceivedNetMessage(msg.dstChannel, msg);
                }
            }
        }

        protected override void CarrierProtocolJoin(P2pNetChannelInfo mainChannel, string localId, string localHelloData)
        {
            session = connection.CreateSession();
            connection.Start();
            CarrierProtocolListen(localId);
            OnNetworkJoined(mainChannel, localHelloData);
        }

        protected void OnMessage(IMessage receivedMsg) // for all topics
        {
            ITextMessage txtMsg = receivedMsg as ITextMessage;
            P2pNetMessage p2pMsg = JsonConvert.DeserializeObject<P2pNetMessage>(txtMsg.Text);
            CarrierProtocolAddReceiptTimestamp(p2pMsg);
            lock(queueLock)
                messageQueue.Add(p2pMsg); // queue it up
        }

        protected override void CarrierProtocolLeave()
        {
            session.Close();
            connection.Close();
        }

        protected override void CarrierProtocolSend(P2pNetMessage msg)
        {
            IDestination dest = session.GetTopic(msg.dstChannel);
            IMessageProducer prod = session.CreateProducer(dest);
            prod.DeliveryMode = MsgDeliveryMode.NonPersistent;
            string msgJSON = JsonConvert.SerializeObject(msg);
            prod.Send(session.CreateTextMessage(msgJSON));
        }

        protected override void CarrierProtocolListen(string channel)
        {
            IDestination dest = session.GetTopic(channel);
            IMessageConsumer cons = session.CreateConsumer(dest);
            MessageListener l =  new MessageListener(OnMessage);
            listeningDict[channel] =  l;
            cons.Listener += l;
        }

        protected override void CarrierProtocolStopListening(string channel)
        {
            if (listeningDict.ContainsKey(channel))
            {
                IDestination dest = session.GetTopic(channel);
                IMessageConsumer cons = session.CreateConsumer(dest);
                cons.Listener -= listeningDict[channel];
                listeningDict.Remove(channel);
            }
            else
                logger.Warn($"_StopListening(): Not listening to {channel}");
        }

        protected override string CarrierProtocolNewP2pId() => System.Guid.NewGuid().ToString();

        protected override void CarrierProtocolAddReceiptTimestamp(P2pNetMessage msg) => msg.rcptTime = P2pNetDateTime.NowMs;

    }
}
