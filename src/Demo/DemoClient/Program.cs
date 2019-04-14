using System;
using System.Net;
using System.Text;
using System.Threading;
using Mtc.PlainTcp.Core.Client;

namespace DemoClient
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("input 'exit' to close client or other string to send message");

            var client = new PlainTcpClient();
            client.MessageReceived += msg => Console.WriteLine($"new message: {Encoding.UTF8.GetString(msg)}");
            client.Error += msg => Console.WriteLine(msg);

            client.Start(IPAddress.Loopback, 5555);
            while (true)
            {
                var input = Console.ReadLine();
                if (input == "exit")
                {
                    Console.WriteLine("shutting down the client...");
                    break;
                }

                client.Send(Encoding.UTF8.GetBytes(input));
            }
            client.Stop();
            Console.WriteLine("done");
        }
    }
}
