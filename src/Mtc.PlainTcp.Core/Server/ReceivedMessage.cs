namespace Mtc.PlainTcp.Core.Server
{
    public class ReceivedMessage
    {
        public ReceivedMessage(ConnectedClient client, byte[] payload)
        {
            Client = client;
            Payload = payload;
        }

        public ConnectedClient Client { get; }
        public byte[] Payload { get; }
    }
}
