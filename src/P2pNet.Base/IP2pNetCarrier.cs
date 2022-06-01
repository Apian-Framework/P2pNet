using System.Collections.Generic;

namespace P2pNet
{
    public interface IP2pNetCarrier
    {
        // Carrier Protocol (Transport) specific tasks.
        void Join(P2pNetChannelInfo mainChannel, IP2pNetBase p2pBase, string localHelloData);
        void Listen(string channel);
        void StopListening(string channel);
        void Send(P2pNetMessage msg);
        void Leave();

        void Poll(); // Almost always thisn should do nothing. It's here to allow support for
                     // *really* primitive carriers (like Blabber) that require polling to fetch messages.

    }
}