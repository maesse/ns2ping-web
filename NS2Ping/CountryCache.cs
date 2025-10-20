namespace NS2Ping;
using System.Collections.Concurrent;
using System.Text.Json;


public sealed class CountryCache
{
    class ApiKeysFile
    {
        public string? ipgeolocation_key { get; set; }
    }

    public string? ipgeolocation_key { get; private set; } = null;
    public Dictionary<string, string> Cache = new Dictionary<string, string>();
    private readonly object cachelock = new object();

    private ConcurrentDictionary<string, Task<GeoInfo?>> apiQueries = new ConcurrentDictionary<string, Task<GeoInfo?>>();

    private CountryCache()
    {
        try
        {
            var text = File.ReadAllText(getConfigPath());
            var config = JsonSerializer.Deserialize<ApiKeysFile>(text);
            if (config != null)
            {
                ipgeolocation_key = config.ipgeolocation_key;
            }
        }
        catch (Exception ex)
        {
            PingService._logger.LogError("Exception while trying to read apikeys:\n" + ex);
        }

        try
        {
            var text = File.ReadAllText(getCachePath());
            var countrycache = JsonSerializer.Deserialize<Dictionary<string, string>>(text);
            if (countrycache != null)
            {
                Cache = countrycache;
            }
        }
        catch (Exception ex)
        {
            PingService._logger.LogError("Exception while trying to read geocache:\n" + ex);
        }
    }

    private static string getConfigPath()
    {
        var configPath = Environment.GetEnvironmentVariable("CONFIGPATH");
        var configDir = "config";
        if (Directory.Exists(configPath)) configDir = configPath;
        return Path.Join(configDir, "apikeys.json");
    }

    private static string getCachePath()
    {
        var configPath = Environment.GetEnvironmentVariable("CONFIGPATH");
        var configDir = "config";
        if (Directory.Exists(configPath)) configDir = configPath;
        return Path.Join(configDir, "geocache.json");
    }

    private void SaveCache()
    {
        PingService._logger.LogInformation("Saving Country Cache");
        JsonSerializerOptions jsonConfig = new()
        {
            WriteIndented = true,
        };
        lock (cachelock)
        {
            var json = JsonSerializer.Serialize(Cache, jsonConfig);
            File.WriteAllText(getCachePath(), json);
        }
    }

    private class GeoInfo
    {
        public string ip { get; set; }
        public string country_code2 { get; set; }
        public GeoInfo(string ip, string country_code2)
        {
            this.ip = ip;
            this.country_code2 = country_code2;
        }
    }

    internal async Task<string?> GetCountryCode(string hostname)
    {
        // Look in cache
        lock (cachelock)
        {
            var value = Cache.GetValueOrDefault(hostname);
            if (value != null) return value;
        }

        if (ipgeolocation_key == null || ipgeolocation_key.Length == 0)
        {
            return null;
        }

        using (var client = new HttpClient())
        {
            PingService._logger.LogInformation("Retrieving country code for ip: " + hostname);
            // Check if there already is a request in flight for this mod
            string geoUrl = $"https://api.ipgeolocation.io/ipgeo?apiKey={ipgeolocation_key}&fields=country_code2&ip={hostname}";
            // Guard against multiple concurrent queries for the same url
            var task = apiQueries.GetOrAdd(geoUrl, client.GetFromJsonAsync<GeoInfo>(geoUrl));
            var resp = await task;
            if (resp != null)
            {
                bool modified = false;
                lock (cachelock)
                {
                    modified = Cache.TryAdd(hostname, resp.country_code2);
                }
                if (modified) SaveCache();
                return resp.country_code2;
            }
        }
        return null;
    }

    private static CountryCache? instance = null;
    public static CountryCache Instance
    {
        get
        {
            if (instance == null)
            {
                instance = new CountryCache();
            }
            return instance;
        }
    }
}
