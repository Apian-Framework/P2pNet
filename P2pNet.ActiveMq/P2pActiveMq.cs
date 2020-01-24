using System;
using System.Collections.Generic;
using Apache.NMS;
using Apache.NMS.ActiveMQ;
using Newtonsoft.Json;

namespace P2pNet
{

    // This might be a stupid way to do loopback,
    // especially since messages to mainChannel and localId are already handled
    // in the base class
    public class P2pActiveMq : P2pNetBase
    {  
        private IConnection connection;
        private ISession session;      
        // private class ProdConsPair
        // {
        //     public IMessageProducer producer;
        //     public IMessageConsumer consumer;
        //     public ProdConsPair(IMessageProducer p, IMessageConsumer c) {producer=p; consumer=c;}
        // }

        Dictionary<string, MessageListener> listeningDict;

        private List<P2pNetMessage> messageQueue;
        private readonly object queueLock = new object();           

        public P2pActiveMq(IP2pNetClient _client, string _connectionString,  Dictionary<string, string> _config = null) : base(_client, _connectionString,  _config)
        {
            messageQueue = new List<P2pNetMessage>();
            listeningDict = new Dictionary<string, MessageListener>();

            // string brokerUri = $"username,password,activemq:tcp://hostname:61616"; 
            string[] parts = _connectionString.Split(new string[]{","},StringSplitOptions.None);            
            IConnectionFactory factory = new ConnectionFactory(parts[2]);
            connection = factory.CreateConnection(parts[0], parts[1]);
            session = connection.CreateSession();   
            connection.Start();         
        }

        protected override void _Poll()
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
                    _OnReceivedNetMessage(msg.dstChannel, msg);                   
                }
            }
        }

        protected override void _Join(string mainChannel)
        {         
            _Listen(mainChannel);  
            _Listen(localId);                                    
        }

        protected void _OnMessage(IMessage receivedMsg) // for all topics
        {
            ITextMessage txtMsg = receivedMsg as ITextMessage;
            P2pNetMessage p2pMsg = JsonConvert.DeserializeObject<P2pNetMessage>(txtMsg.Text);
            _AddReceiptTimestamp(p2pMsg);
            lock(queueLock)
                messageQueue.Add(p2pMsg); // queue it up            
        }

        protected override void _Leave()
        {
            session.Close();
            connection.Close();
        }
        protected override bool _Send(P2pNetMessage msg)
        {
            IDestination dest = session.GetTopic(msg.dstChannel);
            IMessageProducer prod = session.CreateProducer(dest); 
            prod.DeliveryMode = MsgDeliveryMode.NonPersistent;
            string msgJSON = JsonConvert.SerializeObject(msg);                
            prod.Send(session.CreateTextMessage(msgJSON));
  
            return true;
        }

        protected override void _Listen(string channel)
        {                    
            IDestination dest = session.GetTopic(channel);            
            IMessageConsumer cons = session.CreateConsumer(dest);
            MessageListener l =  new MessageListener(_OnMessage);
            listeningDict[channel] =  l;
            cons.Listener += l;
        }

        protected override void _StopListening(string channel)
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

        protected override string _NewP2pId() => System.Guid.NewGuid().ToString();
        protected override void _AddReceiptTimestamp(P2pNetMessage msg) => msg.rcptTime = nowMs;        

    }
}
