namespace NS2Ping;
using System.Globalization;

public class ServerRules
{
    public Dictionary<string, string> Rules { get; private set; } = new Dictionary<string, string>();
    public ServerRules(byte[] data)
    {
        ReadInfo(data);
    }

    internal async void ReadInfo(byte[] data)
    {
        var reader = new Reader(data);
        reader.Skip(4);
        byte header = reader.ReadByte();
        short numEntries = reader.ReadShort();

        Rules.Clear();

        for (int i = 0; i < numEntries; i++)
        {
            var name = reader.ReadUTF8String();
            var value = reader.ReadUTF8String();
            Rules.Add(name, value);
        }

        var modList = new List<string>();

        // Get mod titles
        foreach (var entry in Rules)
        {
            if (entry.Key.StartsWith("mods["))
            {
                var entries = entry.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var modid_hex in entries)
                {
                    uint modid;
                    if (!uint.TryParse(modid_hex, NumberStyles.HexNumber, null, out modid))
                    {
                        modList.Add(modid_hex);
                        continue;
                    }


                    var result = await ModCache.Instance.GetModName(modid);
                    if (result != null)
                    {
                        var modName = result.Replace(';', ':');
                        modList.Add(modName);
                    }
                    else
                    {
                        modList.Add(modid_hex);
                    }
                }
                Rules.Remove(entry.Key);
            }
        }

        Rules.Add("modlist", string.Join(';', modList));
    }


}