using System.Net;
using System.Net.Sockets;

namespace MyApp
{
    public class MasterServerQuery
    {
        public IPEndPoint EndPoint { get; private set; }

        public DateTime LastMasterQuery = DateTime.Now - TimeSpan.FromDays(1);
        public TimeSpan MasterQueryFrequency = TimeSpan.FromHours(1);
        public TimeSpan MasterQueryTimeout = TimeSpan.FromMinutes(1);
        private bool isRequestInFlight = false;
        private static readonly IPEndPoint EndOfMessage = new IPEndPoint(0, 0);
        public List<IPEndPoint> ServerList { get; }


        public MasterServerQuery()
        {
            ServerList = new List<IPEndPoint>();

            // Resolve IP of the master query server
            var address = Dns.GetHostEntry("hl2master.steampowered.com").AddressList.First(addr => addr.AddressFamily == AddressFamily.InterNetwork);
            EndPoint = new IPEndPoint(address.MapToIPv4(), 27011);
        }

        public bool ReadyForRefresh()
        {
            if (isRequestInFlight && DateTime.Now - LastMasterQuery > MasterQueryTimeout)
            {
                Console.WriteLine("Master server query timed out or never finished");
                LastMasterQuery = DateTime.Now - TimeSpan.FromDays(1);
            }

            return DateTime.Now - LastMasterQuery > MasterQueryFrequency;
        }

        public void StartRefresh(UdpClient client)
        {
            LastMasterQuery = DateTime.Now;
            isRequestInFlight = true;
            ServerList.Clear();
            SendQueryRequest(client, EndOfMessage);
        }

        private void SendQueryRequest(UdpClient client, IPEndPoint startFrom)
        {
            // Create request payload
            byte[] payload = new byte[] { 0x31, 0xFF };
            payload = Writer.AppendCString(payload, startFrom.ToString());
            payload = Writer.AppendCString(payload, @"\appid\4920");

            int bytesSent = client.Send(payload, payload.Length, EndPoint);
            if (bytesSent != payload.Length) throw new Exception($"UDP send failed, bytesSent({bytesSent}) != payload.Length({payload.Length})");
        }

        public bool HandleResponse(byte[] data, UdpClient client)
        {
            if (!isRequestInFlight)
            {
                Console.WriteLine("Received Master response, but there is no request in flight");
                return false;
            }

            var reader = new Reader(data);

            // Check header
            byte[] magic = { 0xFF, 0xFF, 0xFF, 0xFF, 0x66, 0x0A };
            foreach (byte b in magic)
            {
                if (b != reader.ReadByte())
                {
                    Console.WriteLine("Invalid header from master response!");
                    return false;
                }
            }
            if (data.Length % magic.Length != 0)
            {
                Console.WriteLine("Invalid master response length");
            }

            IPEndPoint? address = null;
            // Read ip addresses from response
            while (reader.Remaining() > 0)
            {
                address = new IPEndPoint(new IPAddress(reader.ReadBytes(4)), reader.ReadUShortBE());
                if (IPEndPoint.Equals(address, EndOfMessage))
                {
                    Console.WriteLine("Completed master query!");
                    isRequestInFlight = false;
                    return true;
                }
                else
                {
                    ServerList.Add(address);
                }
            }

            if (address != null)
            {
                // Query next batch of servers
                SendQueryRequest(client, address);
            }

            return false;
        }
    }
}