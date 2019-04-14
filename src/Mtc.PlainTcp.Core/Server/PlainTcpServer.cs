using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Mtc.PlainTcp.Core.Common;

namespace Mtc.PlainTcp.Core.Server
{
    public class PlainTcpServer : IPlainTcpServer
    {
        private volatile bool _listening;

        private const int ConnectionsLimit = 100;
        private const int BufferSize = 4096;
        private const int SizeHeaderLength = sizeof(int);

        private readonly Socket _listener;

        private readonly SocketAsyncEventArgs _acceptArgs;
        private readonly Pool<SocketAsyncEventArgs> _receiveArgs;
        private readonly Pool<SocketAsyncEventArgs> _sendArgs;

        private readonly ConcurrentDictionary<Socket, ConcurrentQueue<SocketAsyncEventArgs>> _receiveQueue;
        private readonly ConcurrentDictionary<Socket, ConnectedClient> _clients;

        public PlainTcpServer(IPAddress ipAddress, int port)
        {
            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _listener.Bind(new IPEndPoint(ipAddress, port));

            _acceptArgs = new SocketAsyncEventArgs();
            _acceptArgs.Completed += SocketOperationCompleted;

            _receiveArgs = new Pool<SocketAsyncEventArgs>(10, () =>
            {
                var args = new SocketAsyncEventArgs();
                args.Completed += SocketOperationCompleted;
                args.SetBuffer(new byte[BufferSize], 0, BufferSize);
                return args;
            });
            _sendArgs = new Pool<SocketAsyncEventArgs>(10, () =>
            {
                var args = new SocketAsyncEventArgs();
                args.Completed += SocketOperationCompleted;
                return args;
            });

            _receiveQueue = new ConcurrentDictionary<Socket, ConcurrentQueue<SocketAsyncEventArgs>>();
            _clients = new ConcurrentDictionary<Socket, ConnectedClient>();
        }

        public void Start()
        {
            _listening = true;

            _listener.Listen(ConnectionsLimit);
            if (!_listener.AcceptAsync(_acceptArgs))
            {
                SocketOperationCompleted(_listener, _acceptArgs);
            }

            //start keep alive
        }

        public void Stop()
        {
            _listener.Shutdown(SocketShutdown.Both);
            _listener.Close();
        }

        public void Broadcast(byte[] message)
        {
            var msg = new byte[message.Length + SizeHeaderLength];
            Buffer.BlockCopy(BitConverter.GetBytes(message.Length), 0, msg, 0, SizeHeaderLength);
            Buffer.BlockCopy(message, 0, msg, SizeHeaderLength, message.Length);

            var clients = _clients.Values;
            foreach (var client in clients)
            {
                var args = _sendArgs.Get();
                args.SetBuffer(msg, 0, msg.Length);
                if (!client.Socket.SendAsync(args))
                {
                    SocketOperationCompleted(client.Socket, args);
                }
            }
        }

        public void Send(ConnectedClient client, byte[] message)
        {
            var msg = new byte[message.Length + SizeHeaderLength];
            Buffer.BlockCopy(BitConverter.GetBytes(message.Length), 0, msg, 0, SizeHeaderLength);
            Buffer.BlockCopy(message, 0, msg, SizeHeaderLength, message.Length);

            var args = _sendArgs.Get();
            args.SetBuffer(msg, 0, msg.Length);
            if (!client.Socket.SendAsync(args))
            {
                SocketOperationCompleted(client.Socket, args);
            }
        }

        public event Action<ConnectedClient> ClientConnected;

        public event Action<ConnectedClient> ClientDisconnected;

        public event Action<ReceivedMessage> MessageReceived;

        private void SocketOperationCompleted(object sender, SocketAsyncEventArgs args)
        {
            if (args.SocketError != SocketError.Success)
            {
                //todo disconnect socket
            }

            switch (args.LastOperation)
            {
                case SocketAsyncOperation.Accept:
                    AcceptCompleted((Socket)sender, args);
                    break;
                case SocketAsyncOperation.Receive:
                    ReceiveCompleted((Socket)sender, args);
                    break;
                case SocketAsyncOperation.Send:
                    SendCompleted((Socket)sender, args);
                    break;
            }
        }

        private void AcceptCompleted(Socket socket, SocketAsyncEventArgs args)
        {
            var clientSocket = args.AcceptSocket;
            var client = new ConnectedClient(clientSocket);
            _clients.TryAdd(clientSocket, client);
            _receiveQueue.TryAdd(clientSocket, new ConcurrentQueue<SocketAsyncEventArgs>());

            ClientConnected?.Invoke(client);

            _acceptArgs.AcceptSocket = null;
            if (!_listener.AcceptAsync(_acceptArgs))
            {
                SocketOperationCompleted(_listener, _acceptArgs);
            }

            var receiveArgs = _receiveArgs.Get();
            if (!clientSocket.ReceiveAsync(receiveArgs))
            {
                SocketOperationCompleted(clientSocket, receiveArgs);
            }

            ProcessClientMessages(clientSocket);
        }

        private void ReceiveCompleted(Socket socket, SocketAsyncEventArgs args)
        {
            _receiveQueue[socket].Enqueue(args);
            var newArgs = _receiveArgs.Get();
            if (!socket.ReceiveAsync(newArgs))
            {
                SocketOperationCompleted(socket, newArgs);
            }
        }

        private void SendCompleted(Socket socket, SocketAsyncEventArgs args)
        {
            _sendArgs.Return(args);
        }

        private void ProcessClientMessages(Socket socket)
        {
            if (!_receiveQueue.TryGetValue(socket, out var receiveArgs))
            {
                return;
            }

            var headerOffset = 0; //current header read
            var msgOffset = 0; //current message read
            var msgLength = 0; //payload size
            var rawHeader = new byte[SizeHeaderLength];
            byte[] rawMessage = null;

            while (_listening)
            {
                if (!receiveArgs.TryDequeue(out var args))
                {
                    continue;
                }

                if (!socket.Connected)
                {
                    return;
                }

                var buffer = args.Buffer;
                var bytesTransferred = args.BytesTransferred;
                var bufferOffset = 0;

                if (buffer.Length == 0 || bytesTransferred == 0)
                {
                    continue;
                }

                while (bufferOffset < bytesTransferred)
                {
                    //read header
                    if (headerOffset < SizeHeaderLength)
                    {
                        var toReadForHeader = Math.Min(SizeHeaderLength - headerOffset, bytesTransferred - bufferOffset);
                        Buffer.BlockCopy(buffer, bufferOffset, rawHeader, headerOffset, toReadForHeader);
                        bufferOffset += toReadForHeader;
                        headerOffset += toReadForHeader;

                        if (headerOffset == SizeHeaderLength)
                        {
                            msgLength = BitConverter.ToInt32(rawHeader, 0);
                        }
                        continue;
                    }

                    //read payload
                    if (rawMessage == null)
                    {
                        rawMessage = new byte[msgLength];
                    }
                    var toReadForMsg = Math.Min(msgLength - msgOffset, bytesTransferred - bufferOffset);
                    Buffer.BlockCopy(buffer, bufferOffset, rawMessage, msgOffset, toReadForMsg);
                    bufferOffset += toReadForMsg;
                    msgOffset += toReadForMsg;

                    if (msgOffset == msgLength)
                    {
                        CompleteMessage(socket, rawMessage);

                        headerOffset = 0;
                        msgOffset = 0;
                        msgLength = 0;
                        rawMessage = null;
                    }
                }

                _receiveArgs.Return(args);
            }
        }

        private void CompleteMessage(Socket socket, byte[] payload)
        {
            MessageReceived?.Invoke(new ReceivedMessage(_clients[socket], payload));
        }
    }
}
