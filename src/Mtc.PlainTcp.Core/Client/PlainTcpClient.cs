using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Mtc.PlainTcp.Core.Common;

namespace Mtc.PlainTcp.Core.Client
{
    public class PlainTcpClient : IPlainTcpClient
    {
        private readonly Socket _client;
        private volatile bool _working;

        private const int BufferSize = 4096;
        private const int SizeHeaderLength = sizeof(int);

        private readonly Pool<SocketAsyncEventArgs> _receiveArgs;
        private readonly Pool<SocketAsyncEventArgs> _sendArgs;

        private readonly ConcurrentQueue<SocketAsyncEventArgs> _receiveQueue;

        public PlainTcpClient()
        {
            _client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

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

            _receiveQueue = new ConcurrentQueue<SocketAsyncEventArgs>();
        }

        public void Start(IPAddress ipAddress, int port)
        {
            _client.Connect(ipAddress, port);
            _working = true;

            var args = _receiveArgs.Get();
            if (!_client.ReceiveAsync(args))
            {
                SocketOperationCompleted(_client, args);
            }

            Task.Run(() => ProcessServerMessages(_client));
        }

        public void Stop()
        {
            _working = false;
            _client.Shutdown(SocketShutdown.Both);
            _client.Close();
        }

        public void Send(byte[] message)
        {
            var msg = new byte[message.Length + SizeHeaderLength];
            Buffer.BlockCopy(BitConverter.GetBytes(message.Length), 0, msg, 0, SizeHeaderLength);
            Buffer.BlockCopy(message, 0, msg, SizeHeaderLength, message.Length);

            var args = _sendArgs.Get();
            args.SetBuffer(msg, 0, msg.Length);
            if (!_client.SendAsync(args))
            {
                SocketOperationCompleted(_client, args);
            }
        }

        public event Action<byte[]> MessageReceived;

        private void SocketOperationCompleted(object sender, SocketAsyncEventArgs args)
        {
            if (args.SocketError != SocketError.Success)
            {
                //todo disconnect socket
            }

            switch (args.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ReceiveCompleted((Socket)sender, args);
                    break;
                case SocketAsyncOperation.Send:
                    SendCompleted((Socket)sender, args);
                    break;
            }
        }

        private void ReceiveCompleted(Socket socket, SocketAsyncEventArgs args)
        {
            _receiveQueue.Enqueue(args);
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

        private void ProcessServerMessages(Socket socket)
        {
            var headerOffset = 0; //current header read
            var msgOffset = 0; //current message read
            var msgLength = 0; //payload size
            var rawHeader = new byte[SizeHeaderLength];
            byte[] rawMessage = null;

            while (_working)
            {
                if (!_receiveQueue.TryDequeue(out var args))
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
                        CompleteMessage(rawMessage);

                        headerOffset = 0;
                        msgOffset = 0;
                        msgLength = 0;
                        rawMessage = null;
                    }
                }

                _receiveArgs.Return(args);
            }
        }

        private void CompleteMessage(byte[] payload)
        {
            MessageReceived?.Invoke(payload);
        }
    }
}
