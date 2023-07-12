using System.Text.Json;

namespace MyApp
{
    public sealed class CountryCache
    {
        class ApiKeysFile
        {
            public string? ipgeolocation_key { get; set; }
        }

        public string? ipgeolocation_key { get; private set; } = null;
        public Dictionary<string, string> Cache = new Dictionary<string, string>();
        private readonly object writelock = new object();



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
                Console.WriteLine("Exception while trying to read apikeys:\n" + ex);
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
                Console.WriteLine("Exception while trying to read geocache:\n" + ex);
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
            Console.WriteLine("Saving Country Cache");
            JsonSerializerOptions jsonConfig = new()
            {
                WriteIndented = true,
            };
            lock (writelock)
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
            var value = Cache.GetValueOrDefault(hostname);
            if (value != null) return value;

            if (ipgeolocation_key == null || ipgeolocation_key.Length == 0)
            {
                return null;
            }

            using (var client = new HttpClient())
            {
                Console.WriteLine("Retrieving country code for ip: " + hostname);
                string geoUrl = $"https://api.ipgeolocation.io/ipgeo?apiKey={ipgeolocation_key}&fields=country_code2&ip={hostname}";
                var resp = await client.GetFromJsonAsync<GeoInfo>(geoUrl);
                if (resp != null)
                {
                    if (Cache.TryAdd(hostname, resp.country_code2))
                    {
                        SaveCache();
                    }
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
}