using System;
using System.Net;
using System.Text;
using Mtk.PlainTcp.Core.Server;

namespace DemoServer
{
    class Program
    {
        static void Main()
        {
            Console.WriteLine("input 'q' to close server or other string to broadcast");

            var server = new PlainTcpServer(IPAddress.Loopback, 5555);
            server.ClientConnected += cl => Console.WriteLine($"connected client: {cl.Id}, {cl.Socket.LocalEndPoint}");
            server.Error += msg => Console.WriteLine($"error: {msg}");
            server.MessageReceived += msg =>
            {
                var parsedMsg = Encoding.UTF8.GetString(msg.Payload);
                Console.WriteLine($"new message from {msg.Client.Socket.RemoteEndPoint}, content: {parsedMsg}");
                server.Send(msg.Client, Encoding.UTF8.GetBytes($"ECHO: {parsedMsg}"));
            };
            server.Start();
            while (true)
            {
                var input = Console.ReadLine();
                if (input == "q")
                {
                    Console.WriteLine("shutting down the server...");
                    break;
                }

                server.Broadcast(Encoding.UTF8.GetBytes(input));
            }
            server.Stop();
            Console.WriteLine("done");
        }
    }
}
