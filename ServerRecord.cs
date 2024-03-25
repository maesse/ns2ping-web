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
        public PlayerInfo? PlayerInfo { get; set; }
        public ServerRules? Rules { get; set; }
        public IPEndPoint EndPoint { get; set; }
        public int lastRequestPingTime { get; private set; }
        public bool NotResponding { get; private set; }
        public bool HasChanges { get; set; }
        public string? CountryCode { get; set; } = null;
        private DateTime lastRequestTime = DateTime.Now - TimeSpan.FromSeconds(10);
        private byte[]? challenge;
        private bool infoRequestInFlight = false;
        private bool wasJoinable = false;
        private int? maxJoinableSpec = 3;
        private DateTime lastJoinable = DateTime.Now - TimeSpan.FromSeconds(10);
        private readonly ILogger _logger;

        public ServerRecord(ILogger logger, string host, int? specLimit = null)
        {
            maxJoinableSpec = specLimit;
            _logger = logger;
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
            NotResponding = true;

            GetCountryInformation();
        }

        public bool HasValidData()
        {
            return !NotResponding && info != null;
        }

        private async void GetCountryInformation()
        {
            CountryCode = await CountryCache.Instance.GetCountryCode(Hostname);
            HasChanges = true;
        }

        private void SendRulesRequest(UdpClient client)
        {
            // We should have a challenge value at this point
            if (challenge == null)
            {
                _logger.LogWarning("SendRulesRequest() failed because no challenge value was found. Host: " + EndPoint);
                return;
            }

            // Header + 'V'
            byte[] payload = { 0xff, 0xff, 0xff, 0xff, 0x56 };
            payload = Writer.Append(payload, challenge);

            int bytesSent = client.Send(payload, payload.Length, EndPoint);
            NetworkStats.SentBytes(bytesSent);
            if (bytesSent != payload.Length) throw new Exception($"UDP send failed, bytesSent({bytesSent}) != payload.Length({payload.Length})");

            lastRequestTime = DateTime.Now;
            _logger.LogDebug($"Sending A2S_RULES to server {EndPoint}");
        }

        private void SendPlayerRequest(UdpClient client)
        {
            // We should have a challenge value at this point
            if (challenge == null)
            {
                _logger.LogWarning("SendPlayerRequest() failed because no challenge value was found. Host: " + EndPoint);
                return;
            }

            // Header + 'U'
            byte[] payload = { 0xff, 0xff, 0xff, 0xff, 0x55 };
            payload = Writer.Append(payload, challenge);

            int bytesSent = client.Send(payload, payload.Length, EndPoint);
            NetworkStats.SentBytes(bytesSent);
            if (bytesSent != payload.Length) throw new Exception($"UDP send failed, bytesSent({bytesSent}) != payload.Length({payload.Length})");

            lastRequestTime = DateTime.Now;
            _logger.LogDebug($"Sending A2S_PLAYER to server {EndPoint}");
        }

        public void SendInfoRequest(UdpClient client)
        {
            if (infoRequestInFlight)
            {
                NotResponding = true;
                lastRequestPingTime = 999;
            }

            byte[] payload = { 0xff, 0xff, 0xff, 0xff };
            payload = Writer.AppendCString(payload, "TSource Engine Query");
            if (challenge != null)
            {
                payload = Writer.Append(payload, challenge);
            }

            int bytesSent = client.Send(payload, payload.Length, EndPoint);
            NetworkStats.SentBytes(bytesSent);
            if (bytesSent != payload.Length) throw new Exception($"UDP send failed, bytesSent({bytesSent}) != payload.Length({payload.Length})");

            lastRequestTime = DateTime.Now;
            infoRequestInFlight = true;
            _logger.LogDebug($"Sending InfoRequest to server {EndPoint}");
        }

        public bool IsJoinable()
        {
            if (!HasValidData())
            {
                return false;
            }

            int maxSpec = info!.GetMaxSpectators();
            if (maxSpec == 5) maxSpec = 3;
            maxSpec = maxJoinableSpec ?? maxSpec;
            if (info.GetPlayersIngame() + info.GetSpectators() < info.MaxPlayers + maxSpec) return true;

            return false;
        }

        internal bool IsReadyForRefresh()
        {
            int msSinceLastRequest = (int)(DateTime.Now - lastRequestTime).TotalMilliseconds;
            if (!infoRequestInFlight)
            {
                if (info != null && info.GetPlayersIngame() == 0)
                {
                    // Throttle down on empty servers
                    return msSinceLastRequest > 10000;
                }
                else
                {
                    return msSinceLastRequest > 1000;
                }

            }
            else
            {
                return msSinceLastRequest > 10000;
            }
        }

        public bool RecentlyWentJoinable()
        {
            return (DateTime.Now - lastJoinable).TotalSeconds < 10;
        }

        internal void HandleResponse(byte[] data, UdpClient client)
        {
            lastRequestPingTime = (int)(DateTime.Now - lastRequestTime).TotalMilliseconds;
            NotResponding = false;

            if (data[4] == 'A')
            {
                if (!infoRequestInFlight)
                {
                    _logger.LogWarning("Received response, but there is no request in flight"); // This breaks now since there can be multple requests in flight at the same time
                }
                // Resend request with challenge
                _logger.LogDebug($"Received new challenge for server {EndPoint}");
                challenge = new byte[4];
                Buffer.BlockCopy(data, 5, challenge, 0, 4);
                infoRequestInFlight = false;
                SendInfoRequest(client);
            }
            else if (data[4] == 'I')
            {
                if (!infoRequestInFlight)
                {
                    _logger.LogWarning("Received response, but there is no request in flight"); // This breaks now since there can be multple requests in flight at the same time
                }
                infoRequestInFlight = false;
                // Received A2S_INFO response
                if (info == null)
                {
                    info = new A2SInfo(data);
                    wasJoinable = IsJoinable();
                    HasChanges = true;
                    SendRulesRequest(client);
                    SendPlayerRequest(client);
                }
                else
                {
                    string lastMapInfo = info.Map;
                    if (info.ReadInfo(data))
                    {
                        HasChanges = true;
                        SendPlayerRequest(client);
                        if (!string.Equals(lastMapInfo, info.Map))
                        {
                            // Get new rules if map has changed
                            SendRulesRequest(client);
                        }
                    }

                    bool newJoinable = IsJoinable();
                    if (newJoinable && !wasJoinable)
                    {
                        HasChanges = true;
                        lastJoinable = DateTime.Now;
                    }
                    wasJoinable = newJoinable;
                }
            }
            else if (data[4] == 'D')
            {
                // Received A2S_PLAYER response
                if (PlayerInfo == null)
                {
                    PlayerInfo = new PlayerInfo(data);
                }
                else
                {
                    PlayerInfo.ReadInfo(data);
                }
            }
            else if (data[4] == 'E')
            {
                // Received A2S_RULES response
                if (Rules == null)
                {
                    Rules = new ServerRules(data);
                }
                else
                {
                    Rules.ReadInfo(data);
                }
            }
            else
            {
                _logger.LogWarning("Invalid response..");
            }
        }

        internal void ResetChallenge()
        {
            challenge = null;
        }


    }
}
