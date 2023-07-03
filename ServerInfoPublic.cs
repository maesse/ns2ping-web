using MyApp;

public class ServerInfoPublic
{
    public int ID { get; private set; }
    public bool Joinable { get; private set; }
    public bool RecentlyWentJoinable { get; private set; }
    public int? PlayersIngame { get; private set; }
    public int? MaxPlayers { get; private set; }
    public int? Spectators { get; private set; }
    public int? MaxSpectators { get; private set; }
    public string? MMR { get; private set; }
    public string? MapName { get; private set; }
    public string Hostname { get; private set; }
    public int Port { get; private set; }
    public string? ServerName { get; private set; }
    public int Ping { get; private set; }
    public string CountryCode { get; private set; }
    public string CountryFlag { get; private set; }

    public ServerInfoPublic(ServerRecord record)
    {
        ID = record.ID;
        Joinable = record.IsJoinable();
        RecentlyWentJoinable = record.RecentlyWentJoinable();
        Hostname = record.Hostname;
        Port = record.Port;
        Ping = record.lastRequestPingTime;
        CountryCode = record.CountryCode;
        CountryFlag = record.CountryImg;
        if (record.info != null)
        {
            PlayersIngame = record.info.GetPlayersIngame();
            MaxPlayers = record.info.MaxPlayers;
            Spectators = record.info.GetSpectators();
            MaxSpectators = record.info.GetMaxSpectators();
            MMR = record.info.GetMMR();
            MapName = record.info.Map;
            ServerName = record.info.Name;
        }
    }
}