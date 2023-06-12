using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MyApp
{
    public class ServerRecord
    {
        private static int ID_COUNTER = 0;
        public int ID { get; }
        public string Hostname { get; }
        public int Port { get; set; }
        public A2SInfo? info { get; set; }
        public IPEndPoint EndPoint;
        public int lastRequestPingTime { get; private set; }
        public bool HasChanges { get; set; }

        private DateTime lastRequestTime = DateTime.Now - TimeSpan.FromSeconds(10);
        private byte[]? challenge;
        private bool requestInFlight = false;
        private bool wasJoinable = false;
        private int? maxJoinableSpec = null;
        private DateTime lastJoinable = DateTime.Now - TimeSpan.FromSeconds(10);

        public ServerRecord(string host, int? specLimit) : this(host)
        {
            maxJoinableSpec = specLimit;
        }

        public ServerDescription GetDescription()
        {
            var desc = new ServerDescription();
            desc.Hostname = Hostname + ":" + Port;
            desc.MaxJoinableSpec = maxJoinableSpec;
            return desc;
        }

        public ServerRecord(string host)
        {
            ID = ID_COUNTER++;
            Hostname = host;
            if (host.Contains(":"))
            {
                string[] hostParts = host.Split(':');

                if (hostParts.Length == 2)
                {
                    Hostname = hostParts[0];
                    int port;
                    if (int.TryParse(hostParts[1], out port))
                    {
                        Port = port;
                    }
                }
            }

            EndPoint = new IPEndPoint(IPAddress.Parse(Hostname), Port + 1);
            lastRequestPingTime = 999;
        }

        public void SendInfoRequest(UdpClient client)
        {
            if (requestInFlight)
            {
                lastRequestPingTime = 999;
            }

            byte[] payload = { 0xff, 0xff, 0xff, 0xff };
            payload = Writer.AppendCString(payload, "TSource Engine Query");
            if (challenge != null)
            {
                payload = Writer.Append(payload, challenge);
            }

            int bytesSent = client.Send(payload, payload.Length, EndPoint);
            if (bytesSent != payload.Length) throw new Exception($"UDP send failed, bytesSent({bytesSent}) != payload.Length({payload.Length})");

            lastRequestTime = DateTime.Now;
            requestInFlight = true;
        }

        public bool IsJoinable()
        {
            if (lastRequestPingTime >= 999 || info == null)
            {
                return false;
            }

            int maxSpec = info.GetMaxSpectators();
            if (maxSpec == 5) maxSpec = 3;
            if (maxJoinableSpec.HasValue) maxSpec = maxJoinableSpec.Value;
            if (info.GetPlayersIngame() + info.GetSpectators() < info.MaxPlayers + maxSpec) return true;

            return false;
        }



        internal bool IsReadyForRefresh()
        {
            int msSinceLastRequest = (int)(DateTime.Now - lastRequestTime).TotalMilliseconds;
            if (!requestInFlight)
            {
                if (info != null && info.GetPlayersIngame() == 0)
                {
                    // Throttle down on empty servers
                    return msSinceLastRequest > 10000;
                }
                else
                {
                    return msSinceLastRequest > 3000;
                }

            }
            else
            {
                return msSinceLastRequest > 10000;
            }
        }

        public static void PlaySound(string file)
        {
            //Process.Start(@"powershell", $@"-c (New-Object Media.SoundPlayer '{file}').PlaySync();");
        }

        public bool RecentlyWentJoinable()
        {
            return (DateTime.Now - lastJoinable).TotalSeconds < 10;
        }

        internal void HandleResponse(byte[] data, UdpClient client)
        {
            if (!requestInFlight)
            {
                Console.WriteLine("Received response, but there is no request in flight");
                return;
            }

            requestInFlight = false;
            lastRequestPingTime = (int)(DateTime.Now - lastRequestTime).TotalMilliseconds;

            if (data[4] == 'A')
            {
                // Resend request with challenge
                challenge = new byte[4];
                Buffer.BlockCopy(data, 5, challenge, 0, 4);
                SendInfoRequest(client);
            }
            else if (data[4] == 'I')
            {
                if (info == null)
                {
                    info = new A2SInfo(data);
                    wasJoinable = IsJoinable();
                    HasChanges = true;
                }
                else
                {
                    if (info.ReadInfo(data))
                    {
                        HasChanges = true;
                    }

                    bool newJoinable = IsJoinable();
                    if (newJoinable && !wasJoinable)
                    {
                        HasChanges = true;
                        lastJoinable = DateTime.Now;
                        // Play sound
                        PlaySound("mixkit-long-pop-2358.wav");
                    }
                    wasJoinable = newJoinable;
                }


            }
            else
            {
                Console.WriteLine("Invalid response..");
            }
        }
    }
}
