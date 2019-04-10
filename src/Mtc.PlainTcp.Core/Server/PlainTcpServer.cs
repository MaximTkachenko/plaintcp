using System;
using System.Net;
using System.Net.Sockets;
using Mtc.PlainTcp.Core.Common;

namespace Mtc.PlainTcp.Core.Server
{
    public class PlainTcpServer : IPlainTcpServer
    {
        private const int ConnectionsLimit = 100;
        private const int BufferSize = 4096;

        private readonly Socket _listener;
        private readonly SocketAsyncEventArgs _acceptArgs;
        private readonly Pool<SocketAsyncEventArgs> _receiveArgs;

        public PlainTcpServer(IPAddress ipAddress, int port)
        {
            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _listener.Bind(new IPEndPoint(ipAddress, port));

            _acceptArgs = new SocketAsyncEventArgs();
            _acceptArgs.Completed += SocketOperationCompleted;

            _receiveArgs = new Pool<SocketAsyncEventArgs>(10, null, null);
        }

        public void Start()
        {
            _listener.Listen(ConnectionsLimit);
            if (!_listener.AcceptAsync(_acceptArgs))
            {
                SocketOperationCompleted(_listener, _acceptArgs);
            }


        }

        public void Stop()
        {

        }

        public event Action<ConnectedClient> ClientConnected;

        public event Action<ConnectedClient> ClientDicconnected;

        public event Action<ConnectedClient, ReceivedMessage> MessageReceived;

        private void SocketOperationCompleted(object sender, SocketAsyncEventArgs args)
        {
            switch (args.LastOperation)
            {
                case SocketAsyncOperation.Accept:
                    AcceptCompleted(args);
                    break;
                case SocketAsyncOperation.Receive:
                    ReceiveCompleted(args);
                    break;
                case SocketAsyncOperation.Send:
                    SendCompleted(args);
                    break;
            }
        }

        private void AcceptCompleted(SocketAsyncEventArgs args)
        {
            var clientSocket = args.AcceptSocket;
            var client = new ConnectedClient(clientSocket);

            ClientConnected?.Invoke(client);

            _acceptArgs.AcceptSocket = null;
            if (!_listener.AcceptAsync(_acceptArgs))
            {
                SocketOperationCompleted(_listener, _acceptArgs);
            }

            if (!clientSocket.ReceiveAsync(_acceptArgs))
            {
                SocketOperationCompleted(clientSocket, _acceptArgs);
            }

            //todo start processing receive queue
        }

        private void ReceiveCompleted(SocketAsyncEventArgs args)
        {

        }

        private void SendCompleted(SocketAsyncEventArgs args)
        {

        }
    }
}
