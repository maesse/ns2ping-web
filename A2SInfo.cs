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

        public const int EXT_VERSION = 0;
        public const int EXT_GAMETYPE = 1;
        public const int EXT_SPECTATORS = 11;
        public const int EXT_MAXSPECTATORS = 12;
        public const int EXT_PLAYERS_INGAME = 15;
        public const int EXT_MMR = 16;

        public int GetPlayersIngame()
        {
            if (KeywordParts.Count <= EXT_PLAYERS_INGAME) return Players;

            int number;
            int.TryParse(KeywordParts[EXT_PLAYERS_INGAME], out number);
            return number;
        }

        public int GetSpectators()
        {
            if (KeywordParts.Count <= EXT_SPECTATORS) return 0;

            int number;
            int.TryParse(KeywordParts[EXT_SPECTATORS], out number);
            return number;
        }

        public int GetMaxSpectators()
        {
            if (KeywordParts.Count <= EXT_MAXSPECTATORS) return 0;

            int number;
            int.TryParse(KeywordParts[EXT_MAXSPECTATORS], out number);
            return number;
        }

        public string GetMMR()
        {
            string result = "N/A";
            if (KeywordParts.Count > EXT_MMR)
            {
                string val = KeywordParts[EXT_MMR];
                int number;
                if (int.TryParse(val, out number))
                {
                    if (number > 0) result = "" + number;
                }
            }
            return result;
        }

        public bool ReadInfo(byte[] data)
        {
            bool hasChange = false;
            var reader = new Reader(data);
            reader.Skip(4);
            Header = reader.ReadByte();
            Protocol = reader.ReadByte();
            Name = reader.ReadUTF8String();
            string oldMap = Map;
            Map = reader.ReadString();
            if (!Map.Equals(oldMap)) hasChange = true;
            Folder = reader.ReadString();
            Game = reader.ReadString();
            ID = reader.ReadShort();
            byte oldPlayers = Players;
            Players = reader.ReadByte();
            if (Players != oldPlayers) hasChange = true;
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
                            switch (i)
                            {
                                case EXT_GAMETYPE:
                                case EXT_MAXSPECTATORS:
                                case EXT_MMR:
                                case EXT_PLAYERS_INGAME:
                                case EXT_SPECTATORS:
                                case EXT_VERSION:
                                    hasChange = true;
                                    break;
                            }

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

            return hasChange;
        }

    }
}