namespace MyApp
{
    public class A2SInfo
    {
        public byte Header;
        public byte Protocol;
        public string Name;
        public string Map;
        public string Folder;
        public string Game;
        public short ID;
        public byte Players;
        public byte MaxPlayers;
        public byte Bots;
        public byte ServerType;
        public byte Environment;
        public byte Visibility;
        public byte VAC;
        public string Version;
        public byte EDF;
        public short port;
        public long SteamID;
        public string Keywords;
        public List<string> KeywordParts = new List<string>();

        public A2SInfo(byte[] data)
        {
            ReadInfo(data);
        }

        public static readonly int EXT_VERSION = 0;
        public static readonly int EXT_GAMETYPE = 1;
        public static readonly int EXT_SPECTATORS = 11;
        public static readonly int EXT_MAXSPECTATORS = 12;
        public static readonly int EXT_PLAYERS_INGAME = 15;
        public static readonly int EXT_MMR = 16;

        public int GetPlayersIngame()
        {
            int number;
            int.TryParse(KeywordParts[EXT_PLAYERS_INGAME], out number);
            return number;
        }

        public int GetSpectators()
        {
            int number;
            int.TryParse(KeywordParts[EXT_SPECTATORS], out number);
            return number;
        }

        public int GetMaxSpectators()
        {
            int number;
            int.TryParse(KeywordParts[EXT_MAXSPECTATORS], out number);
            return number;
        }

        public string GetMMR()
        {
            string result = "N/A";
            string val = KeywordParts[EXT_MMR];
            int number;
            if (int.TryParse(val, out number))
            {
                if (number > 0) result = "" + number;
            }
            return result;
        }

        public void ReadInfo(byte[] data)
        {
            var reader = new Reader(data);
            reader.Skip(4);
            Header = reader.ReadByte();
            Protocol = reader.ReadByte();
            Name = reader.ReadString();
            Map = reader.ReadString();
            Folder = reader.ReadString();
            Game = reader.ReadString();
            ID = reader.ReadShort();
            Players = reader.ReadByte();
            MaxPlayers = reader.ReadByte();
            Bots = reader.ReadByte();
            ServerType = reader.ReadByte();
            Environment = reader.ReadByte();
            Visibility = reader.ReadByte();
            VAC = reader.ReadByte();
            Version = reader.ReadString();
            EDF = reader.ReadByte();
            if ((EDF & 0x80) != 0) port = reader.ReadShort();
            if ((EDF & 0x10) != 0) SteamID = reader.ReadLong();
            if ((EDF & 0x20) != 0)
            {
                Keywords = reader.ReadString();
                var split = Keywords.Split('|');
                for (int i = 0; i < split.Length; i++)
                {
                    var value = split[i];
                    if (i < KeywordParts!.Count)
                    {
                        // Compare and overwrite
                        if (KeywordParts[i] != value)
                        {
                            //Console.WriteLine($"Parameter {i} changed from {KeywordParts[i]} to {value}");
                        }
                        KeywordParts[i] = value;
                    }
                    else
                    {
                        KeywordParts.Add(value);
                    }
                }
            }
        }

    }
}