namespace NS2Ping;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;

public class MasterServerQueryWeb
{

    public DateTime LastMasterQuery = DateTime.Now - TimeSpan.FromDays(1);
    public TimeSpan MasterQueryFrequency = TimeSpan.FromHours(1);
    public TimeSpan MasterQueryTimeout = TimeSpan.FromMinutes(1);
    public List<IPEndPoint> ServerList { get; } = new List<IPEndPoint>();
    private string? SteamWebAPI { get; set; } = null;

    public MasterServerQueryWeb()
    {
        try
        {
            var text = File.ReadAllText(GetConfigPath());
            var config = JsonSerializer.Deserialize<ApiKeysFile>(text);
            if (config != null)
            {
                SteamWebAPI = config.steamWebAPI;
            }
        }
        catch (Exception ex)
        {
            PingService._logger.LogError("Exception while trying to read apikeys:\n" + ex);
            throw;
        }
    }

    private static string GetConfigPath()
    {
        var configPath = Environment.GetEnvironmentVariable("CONFIGPATH");
        var configDir = "config";
        if (Directory.Exists(configPath)) configDir = configPath;
        return Path.Join(configDir, "apikeys.json");
    }

    public bool ReadyForRefresh()
    {
        if (DateTime.Now - LastMasterQuery > MasterQueryTimeout)
        {
            PingService._logger.LogWarning("Master server query timed out or never finished");
            LastMasterQuery = DateTime.Now - TimeSpan.FromDays(1);
        }

        return DateTime.Now - LastMasterQuery > MasterQueryFrequency;
    }

    public void StartRefresh(UdpClient client)
    {
        LastMasterQuery = DateTime.Now;
        ServerList.Clear();
        if (string.IsNullOrWhiteSpace(SteamWebAPI))
        {
            PingService._logger.LogError("Steam Web API key is missing. Add it to config/apikeys.json as steamWebAPI.");
            return;
        }

        try
        {
            using var http = new HttpClient();
            http.Timeout = MasterQueryTimeout;

            var filter = @"\appid\4920"; // Natural Selection 2 app id filter
            var url = $"https://api.steampowered.com/IGameServersService/GetServerList/v1/?key={SteamWebAPI}&filter={Uri.EscapeDataString(filter)}&limit=5000";

            PingService._logger.LogInformation("Requesting server list from Steam Web API ...");
            var resp = http.GetAsync(url).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                PingService._logger.LogError($"Steam Web API request failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                return;
            }

            var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var root = JsonSerializer.Deserialize<Root>(json);
            var servers = root?.response?.servers;
            if (servers == null)
            {
                PingService._logger.LogWarning("Steam Web API returned no servers.");
                return;
            }

            int added = 0;
            foreach (var s in servers)
            {
                if (string.IsNullOrWhiteSpace(s.addr)) continue;
                try
                {
                    // s.addr is typically "IP:QUERYPORT". PingService will convert to game port by subtracting 1.
                    var lastColon = s.addr.LastIndexOf(':');
                    if (lastColon <= 0 || lastColon >= s.addr.Length - 1) continue;
                    var ipStr = s.addr.Substring(0, lastColon);
                    var portStr = s.addr.Substring(lastColon + 1);
                    if (!IPAddress.TryParse(ipStr, out var ip)) continue;
                    if (!int.TryParse(portStr, out var port)) continue;
                    if (port < IPEndPoint.MinPort || port > IPEndPoint.MaxPort) continue;
                    ServerList.Add(new IPEndPoint(ip, port));
                    added++;
                }
                catch (Exception ex)
                {
                    PingService._logger.LogDebug($"Failed to parse server entry '{s.addr}': {ex.Message}");
                }
            }

            PingService._logger.LogInformation($"Received {added} servers from Steam Web API.");
        }
        catch (Exception ex)
        {
            PingService._logger.LogError("Exception while querying Steam Web API: " + ex);
        }
    }

}

class ApiKeysFile
{
    public string? steamWebAPI { get; set; }
}

public class Response
{
    public List<Server>? servers { get; set; }
}

public class Root
{
    public Response? response { get; set; }
}

public class Server
{
    public string? addr { get; set; }
    public int gameport { get; set; }
    public string? steamid { get; set; }
    public string? name { get; set; }
    public int appid { get; set; }
    public string? gamedir { get; set; }
    public string? version { get; set; }
    public string? product { get; set; }
    public int region { get; set; }
    public int players { get; set; }
    public int max_players { get; set; }
    public int bots { get; set; }
    public string? map { get; set; }
    public bool secure { get; set; }
    public bool dedicated { get; set; }
    public string? os { get; set; }
    public string? gametype { get; set; }
}