using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace Mtc.PlainTcp.Core.Server
{
    public class ConnectedClient
    {
        public ConnectedClient(Socket socket)
        {
            Socket = socket;
            Id = Guid.NewGuid();
        }

        public Socket Socket { get; }
        public Guid Id { get; }
    }
}
