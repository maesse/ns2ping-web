using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MyApp;

public class PingService : BackgroundService
{
    public class WebsocketInstance
    {
        public WebSocket webSocket { get; }
        public TaskCompletionSource<object> socketFinishedTcs { get; }
        public DateTime lastKeepAlive { get; set; }
        public WebsocketInstance(WebSocket webSocket, TaskCompletionSource<object> socketFinishedTcs)
        {
            this.webSocket = webSocket;
            this.socketFinishedTcs = socketFinishedTcs;
            this.lastKeepAlive = DateTime.Now;
        }

        public async Task Read()
        {
            while (!webSocket.CloseStatus.HasValue)
            {
                var buffer = new byte[128];
                var receiveResult = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), CancellationToken.None);
                //Console.WriteLine("Received data from websocket: " + receiveResult);
                lastKeepAlive = DateTime.Now;
            }
        }
    }

    private readonly ILogger<PingService> _logger;

    public UdpClient client;
    public IPEndPoint ep;
    public List<ServerRecord> watchedServers;
    public bool running = false;
    public bool sleeping = false;
    private DateTime lastRequest = DateTime.Now;
    public TimeSpan SleepTimeout = TimeSpan.FromSeconds(10);
    public TimeSpan WebsocketTimeout = TimeSpan.FromSeconds(60);
    private CancellationTokenSource sleepToken = new CancellationTokenSource();
    private MasterServerQuery masterQuery = new MasterServerQuery();
    private static int printCounter = 0;
    private List<WebsocketInstance> webSocketList = new List<WebsocketInstance>();
    private readonly object webSocketListLock = new object();
    public PingService(ILogger<PingService> logger)
    {
        _logger = logger;
        Init();
    }

    private void Init()
    {
        watchedServers = new List<ServerRecord>();
        ep = new IPEndPoint(IPAddress.Any, 0);
        client = new UdpClient(ep);

        //ReadConfig();
    }

    private void ReadConfig()
    {
        bool loadFail = false;
        try
        {
            var configText = File.ReadAllText("config/config.json");
            var config = JsonSerializer.Deserialize<List<ServerDescription>>(configText);
            _logger.LogInformation("Reading config");
            foreach (var descr in config)
            {
                AddServer(new ServerRecord(_logger, descr.Hostname, descr.MaxJoinableSpec));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while trying to read config");
            loadFail = true;
        }

        if (loadFail)
        {
            _logger.LogInformation("Loading default config");
            AddServer(new ServerRecord(_logger, "136.243.135.61:27015")); // BAD #1
            AddServer(new ServerRecord(_logger, "136.243.135.61:27019")); // BAD #2
            AddServer(new ServerRecord(_logger, "136.243.135.61:27065")); // BAD #3
            AddServer(new ServerRecord(_logger, "157.90.129.121:28315", 5)); // TTO

            WriteConfig();
        }
    }

    private void WriteConfig()
    {
        _logger.LogInformation("Writing config");
        var descList = new List<ServerDescription>();
        foreach (var server in watchedServers)
        {
            descList.Add(server.GetDescription());
        }
        JsonSerializerOptions jsonConfig = new()
        {
            WriteIndented = true,

        };
        var json = JsonSerializer.Serialize(descList, jsonConfig);
        File.WriteAllText("config/config.json", json);
    }

    private void AddServer(ServerRecord record)
    {
        watchedServers.Add(record);
    }

    private void StopSleep()
    {
        if (sleeping)
        {
            _logger.LogInformation("PingService waking up!");
            var activeToken = sleepToken;
            sleeping = false;
            // Reset Token
            sleepToken = new CancellationTokenSource();
            client.Close();
            client = new UdpClient(ep);
            activeToken.Cancel();
        }
        lastRequest = DateTime.Now;
    }

    public List<ServerInfoPublic> GetServerInfos()
    {
        StopSleep();

        var list = new List<ServerInfoPublic>();
        foreach (var s in watchedServers)
        {
            if (s.lastRequestPingTime < 999)
            {
                list.Add(new ServerInfoPublic(s));
            }
        }
        return list;
    }

    private List<ServerInfoPublic> GetServerInfosWithChanges()
    {
        StopSleep();

        var list = new List<ServerInfoPublic>();
        foreach (var s in watchedServers)
        {
            if (s.lastRequestPingTime < 999 && s.HasChanges)
            {
                list.Add(new ServerInfoPublic(s));
                s.HasChanges = false;
            }
        }
        return list;
    }

    private void UpdateServers()
    {
        if (masterQuery.ReadyForRefresh())
        {
            masterQuery.StartRefresh(client);
        }

        foreach (var server in watchedServers)
        {
            if (server.IsReadyForRefresh())
            {
                server.SendInfoRequest(client);
            }
        }
    }

    private void HandleNewServers()
    {
        int newCount = 0;
        // Add servers that were discovered
        foreach (var addr in masterQuery.ServerList)
        {
            var result = watchedServers.FirstOrDefault(v => IPEndPoint.Equals(v.EndPoint, addr), null);
            if (result == null)
            {
                // Decrement port by one since we are using the game server port here, not the query port
                var gameaddr = new IPEndPoint(addr.Address, addr.Port - 1);
                watchedServers.Add(new ServerRecord(_logger, gameaddr.ToString()));
                newCount++;
            }
        }
        _logger.LogInformation($"Added {newCount} servers to the watched server list");

        // TODO: Remove servers missing from master list that also are not responding
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        running = true;
        bool allowNewRun = false;
        do
        {
            allowNewRun = false;
            try
            {
                var receiveTask = client.ReceiveAsync();

                while (!stoppingToken.IsCancellationRequested)
                {
                    UpdateServers();

                    // Handle sleep
                    int delayTime = 1000;
                    if (DateTime.Now - lastRequest > SleepTimeout)
                    {
                        delayTime = 1000 * 60 * 24;
                        sleeping = true;
                        //sleepToken = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        _logger.LogInformation("PingService going to sleep");
                    }

                    var delayTask = Task.Delay(delayTime, CancellationTokenSource.CreateLinkedTokenSource(sleepToken.Token, stoppingToken).Token);
                    if (receiveTask.IsFaulted)
                    {
                        _logger.LogDebug("ReceiveTask faulted!");
                        Console.WriteLine(receiveTask.Exception);
                    }
                    if (receiveTask.IsCompleted)
                    {
                        _logger.LogDebug("Does this ever run?");
                        receiveTask = client.ReceiveAsync();
                    }


                    while (await Task.WhenAny(delayTask, receiveTask) == receiveTask)
                    {
                        // Handle packet response
                        var result = receiveTask.Result;
                        _logger.LogTrace($"Handling UDP response from {result.RemoteEndPoint}");
                        if (IPEndPoint.Equals(result.RemoteEndPoint, masterQuery.EndPoint))
                        {
                            if (masterQuery.HandleResponse(result.Buffer, client))
                            {
                                HandleNewServers();
                            }
                        }
                        else
                        {
                            var server = watchedServers.SingleOrDefault(v => IPEndPoint.Equals(v!.EndPoint, result.RemoteEndPoint), null);
                            if (server != null)
                            {
                                server.HandleResponse(result.Buffer, client);
                            }
                        }

                        receiveTask = client.ReceiveAsync();
                    }

                    if (delayTask.IsCanceled)
                    {
                        _logger.LogDebug("DelayTask was cancelled!");
                        receiveTask = client.ReceiveAsync();
                    }

                    UpdateWebsockets();

                    if (printCounter++ % 10 == 0)
                    {
                        PrintServers();
                    }
                }
            }
            catch (SocketException e)
            {
                if (e.ErrorCode == 10055)
                {
                    _logger.LogInformation("Woken from sleep? Waiting 3000ms before retrying");
                    Thread.Sleep(3000);
                }
                _logger.LogError(e, "SocketException, trying to restart");
                allowNewRun = true;
            }
            catch (ObjectDisposedException e)
            {
                _logger.LogError(e, "ObjectDisposedException, trying to restart");
                allowNewRun = true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Other exception -- exiting");
                running = false;
                throw;
            }

            if (allowNewRun)
            {
                _logger.LogDebug("Reinitializing PingService");
                Init();
            }
        } while (allowNewRun);

        _logger.LogDebug("Exiting PingService");
        running = false;
    }

    private void UpdateWebsockets()
    {
        lock (webSocketListLock)
        {
            // Clean closed sockets
            var cleanupList = new List<WebsocketInstance>();
            var now = DateTime.Now;
            foreach (var socket in webSocketList)
            {
                if (socket.socketFinishedTcs.Task.IsCompleted)
                {
                    cleanupList.Add(socket);
                    continue;
                }

                if (now - socket.lastKeepAlive > WebsocketTimeout)
                {
                    _logger.LogInformation("Timeout from webSocket " + socket.webSocket);
                    socket.socketFinishedTcs.SetResult(null);
                    cleanupList.Add(socket);
                }
            }
            foreach (var socket in cleanupList)
            {
                webSocketList.Remove(socket);
            }

            if (webSocketList.Count == 0) return;
        }

        // Gather updated servers
        var serverList = GetServerInfosWithChanges();
        if (serverList.Count == 0) return;

        // push updates to all websockets

        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        var payload = JsonSerializer.Serialize(serverList, options);
        var bytes = Encoding.UTF8.GetBytes(payload);
        var arraySegment = new ArraySegment<byte>(bytes);
        lock (webSocketListLock)
        {
            foreach (var socket in webSocketList)
            {
                socket.webSocket.SendAsync(arraySegment, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
    }

    private void PrintServers()
    {
        string serverInfo = "Servers:\n========\n";
        foreach (var server in watchedServers)
        {
            if (server.lastRequestPingTime < 999 && server.info != null)
            {
                serverInfo += $"{(server.IsJoinable() ? '#' : ' '),1}{(server.RecentlyWentJoinable() ? '!' : ' '),1} | {server.info.GetPlayersIngame(),2}/{server.info.MaxPlayers} ({server.info.GetSpectators()}/{server.info.GetMaxSpectators()}) | MMR {server.info.GetMMR()} \t| {server.info.Map,-20} | {server.Hostname,1}:{server.Port}\t| {server.info.Name}\n";
            }
            else
            {
                serverInfo += $"{server.Hostname}:{server.Port} \t| Not responding\n";
            }
        }
        _logger.LogInformation(serverInfo);
        lock (webSocketListLock)
        {
            _logger.LogInformation($"Current WebSocket clients: {webSocketList.Count}");
        }
    }

    private static async Task<Task> WaitAny(Task task1, Task task2)
    {
        return await Task.WhenAny(task1, task2);
    }

    internal WebsocketInstance AddSocket(WebSocket webSocket, TaskCompletionSource<object> socketFinishedTcs)
    {
        var ws = new WebsocketInstance(webSocket, socketFinishedTcs);
        lock (webSocketListLock)
        {
            webSocketList.Add(ws);
        }
        return ws;
    }
}


