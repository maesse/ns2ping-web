using System.Text.Json;

namespace MyApp
{
    public sealed class ModCache
    {
        class ApiKeysFile
        {
            public string? steamWebAPI { get; set; }
        }

        public string? SteamWebAPI { get; private set; } = null;
        public Dictionary<uint, ResultEntry> Cache = new Dictionary<uint, ResultEntry>();
        private readonly object cachelock = new object();

        private ModCache()
        {
            try
            {
                var text = File.ReadAllText(getConfigPath());
                var config = JsonSerializer.Deserialize<ApiKeysFile>(text);
                if (config != null)
                {
                    SteamWebAPI = config.steamWebAPI;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception while trying to read apikeys:\n" + ex);
            }

            try
            {
                var text = File.ReadAllText(getCachePath());
                var modcache = JsonSerializer.Deserialize<Dictionary<uint, ResultEntry>>(text);
                if (modcache != null)
                {
                    Cache = modcache;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception while trying to read modcache:\n" + ex);
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
            return Path.Join(configDir, "modcache.json");
        }

        private void SaveCache()
        {
            Console.WriteLine("Saving Mod Cache");
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

        class ApiResponse
        {
            public PublishedFileDetails? response { get; set; }
        }

        class PublishedFileDetails
        {
            public List<ResultEntry>? publishedfiledetails { get; set; }
        }

        public class ResultEntry
        {
            public int result { get; set; }
            public string? publishedfileid { get; set; }
            public string? title { get; set; }
            public string? file_description { get; set; }
            public string? preview_url { get; set; }

        }

        public async Task<string?> GetModName(uint id)
        {
            // Look in cache
            lock (cachelock)
            {
                var value = Cache.GetValueOrDefault(id);
                if (value != null) return value.title;
            }

            if (SteamWebAPI == null || SteamWebAPI.Length == 0)
            {
                return "" + id;
            }

            // Try to retrieve value
            using (var client = new HttpClient())
            {
                Console.WriteLine("Retriving modinfo for " + id);
                string url = $"https://api.steampowered.com/IPublishedFileService/GetDetails/v1/?key={SteamWebAPI}&publishedfileids%5B0%5D={id}";
                var resp = await client.GetFromJsonAsync<ApiResponse>(url);
                if (resp != null && resp.response != null && resp.response.publishedfiledetails != null)
                {
                    var list = resp.response.publishedfiledetails;
                    if (list.Count != 1)
                    {
                        Console.WriteLine("Got strange SteamWebAPI response.. " + list.Count);
                    }
                    else
                    {
                        lock (cachelock)
                        {
                            Cache.Add(id, list[0]);
                        }
                        SaveCache();
                        return list[0].title;
                    }
                }
            }

            return "" + id;
        }

        private static ModCache? instance = null;
        public static ModCache Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new ModCache();
                }
                return instance;
            }
        }
    }
}