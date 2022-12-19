using System;
using System.Collections.Generic;
using Apache.NMS;
using Apache.NMS.ActiveMQ;
using Newtonsoft.Json;
using UniLog;

namespace P2pNet
{

    public class P2pActiveMq : IP2pNetCarrier
    {
        private IP2pNetBase p2pBase;
        private string username;
        private string password;
        private readonly IConnectionFactory factory; // persists join/leave
        private IConnection connection;
        private ISession session;
        private Dictionary<string, MessageListener> listeningDict;
        private List<P2pNetMessage> messageQueue;
        private object queueLock = new object();

        public UniLogger logger = UniLogger.GetLogger("P2pNetCarrier");

        public P2pActiveMq( string _connectionString)
        {
            // Example: "username,password,activemq:tcp://hostname:61616";
            string[] parts = _connectionString.Split(new string[]{","},StringSplitOptions.None);
            username = parts[0];
            password = parts[1];
            factory = new ConnectionFactory(parts[2]);
            ResetJoinVars();
        }

        public void Poll()
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
                    p2pBase.OnReceivedNetMessage(msg.dstChannel, msg);
                }
            }
        }

        private void ResetJoinVars()
        {
            messageQueue = new List<P2pNetMessage>();
            listeningDict = new Dictionary<string, MessageListener>();
            connection = null;
            session = null;
        }

        public void Join(P2pNetChannelInfo mainChannel, IP2pNetBase _p2pBase, string localHelloData)
        {
            ResetJoinVars();
            p2pBase = _p2pBase;
            connection = factory.CreateConnection(username, password);
            session = connection.CreateSession();
            connection.Start();
            Listen(p2pBase.LocalId);
            p2pBase.OnNetworkJoined(mainChannel, localHelloData);
        }

        protected void OnMessage(IMessage receivedMsg) // for all topics
        {
            ITextMessage txtMsg = receivedMsg as ITextMessage;
            P2pNetMessage p2pMsg = JsonConvert.DeserializeObject<P2pNetMessage>(txtMsg.Text);
            AddReceiptTimestamp(p2pMsg);
            lock(queueLock)
                messageQueue.Add(p2pMsg); // queue it up
        }

        public void Leave()
        {
            session.Close();
            connection.Close();
            ResetJoinVars();
        }

        public void Send(P2pNetMessage msg)
        {
            IDestination dest = session.GetTopic(msg.dstChannel);
            IMessageProducer prod = session.CreateProducer(dest);
            prod.DeliveryMode = MsgDeliveryMode.NonPersistent;
            string msgJSON = JsonConvert.SerializeObject(msg);
            prod.Send(session.CreateTextMessage(msgJSON));
        }

        public void Listen(string channel)
        {
            IDestination dest = session.GetTopic(channel);
            IMessageConsumer cons = session.CreateConsumer(dest);
            MessageListener l =  new MessageListener(OnMessage);
            listeningDict[channel] =  l;
            cons.Listener += l;
        }

        public void StopListening(string channel)
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

        protected void AddReceiptTimestamp(P2pNetMessage msg) => msg.rcptTime = P2pNetDateTime.NowMs;

    }
}
