using MyApp;

public class PlayerInfo
{
    public class PlayerInfoEntry
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public int Score { get; set; }
        public float Duration { get; set; }
        public PlayerInfoEntry(int Index, string Name, int Score, float Duration)
        {
            this.Index = Index;
            this.Name = Name;
            this.Score = Score;
            this.Duration = Duration;
        }
    }

    public List<PlayerInfoEntry> Entries { get; private set; }
    public PlayerInfo(byte[] data)
    {
        Entries = new List<PlayerInfoEntry>();
        ReadInfo(data);
    }

    internal void ReadInfo(byte[] data)
    {
        var reader = new Reader(data);
        reader.Skip(4);
        byte header = reader.ReadByte();
        byte numEntries = reader.ReadByte();

        Entries.Clear();

        for (int i = 0; i < numEntries; i++)
        {
            byte index = reader.ReadByte(); // always 0? Anyways, just ignore this value
            string name = reader.ReadUTF8String();
            int score = reader.ReadInt();
            float duration = reader.readFloat();
            Entries.Add(new PlayerInfoEntry(i, name, score, duration));
        }
    }
}