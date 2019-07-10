using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mtk.PlainTcp.Core.Client;

namespace DemoClient
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("input 'q' to close client or other string to send message");

            var client = new PlainTcpClient();
            client.MessageReceived += msg => Console.WriteLine($"new message: {Encoding.UTF8.GetString(msg)}");
            client.Error += Console.WriteLine;

            client.Start(IPAddress.Loopback, 5555);

            var cts = new CancellationTokenSource();
            Task.Run(() => Messaging(client, cts.Token), cts.Token);

            while (true)
            {
                var input = Console.ReadLine();
                if (input == "q")
                {
                    Console.WriteLine("shutting down the client...");
                    break;
                }

                client.Send(Encoding.UTF8.GetBytes(input));
            }

            cts.Cancel();
            client.Stop();
            Console.WriteLine("done");
        }

        static void Messaging(PlainTcpClient client, CancellationToken ct)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                Thread.Sleep(1000);
                client.Send(Encoding.UTF8.GetBytes($"{Guid.NewGuid()}_{Guid.NewGuid()}_{Guid.NewGuid()}"));
            }
        }
    }
}
