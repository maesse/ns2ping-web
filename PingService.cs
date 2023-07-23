using System.Diagnostics;
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
        public TaskCompletionSource socketFinishedTcs { get; }
        public DateTime lastKeepAlive { get; set; }
        public WebsocketInstance(WebSocket webSocket, TaskCompletionSource socketFinishedTcs)
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
    private List<ServerRecord> watchedServers;
    private readonly object watchedServersLock = new object();
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
        watchedServers = new List<ServerRecord>();
        ep = new IPEndPoint(IPAddress.Any, 0);
        client = new UdpClient(ep);
    }

    private void Init()
    {
        watchedServers = new List<ServerRecord>();
        ep = new IPEndPoint(IPAddress.Any, 0);
        client = new UdpClient(ep);
    }

    private void AddServer(ServerRecord record)
    {
        lock (watchedServersLock)
        {
            watchedServers.Add(record);
        }
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
        lock (watchedServersLock)
        {
            foreach (var s in watchedServers)
            {
                if (s.HasValidData())
                {
                    list.Add(new ServerInfoPublic(s));
                }
            }
        }
        return list;
    }

    private List<ServerInfoPublic> GetServerInfosWithChanges()
    {
        StopSleep();

        var list = new List<ServerInfoPublic>();
        lock (watchedServersLock)
        {
            foreach (var s in watchedServers)
            {
                if (s.HasValidData() && s.HasChanges)
                {
                    list.Add(new ServerInfoPublic(s));
                    s.HasChanges = false;
                }
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

        lock (watchedServersLock)
        {
            foreach (var server in watchedServers)
            {
                if (server.IsReadyForRefresh())
                {
                    server.SendInfoRequest(client);
                }
            }
        }
    }

    private void HandleNewServers()
    {
        int newCount = 0;
        // Add servers that were discovered
        lock (watchedServersLock)
        {
            foreach (var addr in masterQuery.ServerList)
            {
                var result = watchedServers.SingleOrDefault(v => IPEndPoint.Equals(v!.EndPoint, addr), null);
                if (result == null)
                {
                    // Decrement port by one since we are using the game server port here, not the query port
                    var gameaddr = new IPEndPoint(addr.Address, addr.Port - 1);
                    if (gameaddr.ToString().Equals("157.90.129.121:28315"))
                    {
                        // Special case 5 max spec for TTO
                        watchedServers.Add(new ServerRecord(_logger, gameaddr.ToString(), 5));
                    }
                    else
                    {
                        watchedServers.Add(new ServerRecord(_logger, gameaddr.ToString()));
                    }
                    newCount++;
                }
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
                        _logger.LogWarning("ReceiveTask faulted!");
                        Console.WriteLine(receiveTask.Exception);
                    }
                    if (receiveTask.IsCompleted)
                    {
                        _logger.LogWarning("Does this ever run?");
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
                            lock (watchedServersLock)
                            {
                                var server = watchedServers.SingleOrDefault(v => IPEndPoint.Equals(v!.EndPoint, result.RemoteEndPoint), null);
                                if (server != null)
                                {
                                    server.HandleResponse(result.Buffer, client);
                                }
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
            catch (AggregateException e)
            {
                // Exceptions happening inside async/Task might be wrapped
                e.Handle(inner =>
                {
                    if (inner is SocketException)
                    {
                        var innerEx = (SocketException)inner;
                        if (innerEx.ErrorCode == 10055)
                        {
                            _logger.LogInformation("Woken from sleep? Waiting 3000ms before retrying");
                            Thread.Sleep(3000);
                        }
                        _logger.LogError(innerEx, "SocketException, trying to restart");
                        allowNewRun = true;
                        return true;
                    }
                    else
                    {
                        _logger.LogError(inner, "Other exception -- exiting");
                        running = false;
                        return false;
                    }
                });
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
                    socket.socketFinishedTcs.SetResult();
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
        lock (watchedServersLock)
        {
            foreach (var server in watchedServers)
            {
                if (server.HasValidData())
                {
                    serverInfo += $"{(server.IsJoinable() ? '#' : ' '),1}{(server.RecentlyWentJoinable() ? '!' : ' '),1} | {server.info!.GetPlayersIngame(),2}/{server.info.MaxPlayers} ({server.info.GetSpectators()}/{server.info.GetMaxSpectators()}) | MMR {server.info.GetMMR()} \t| {server.info.Map,-20} | {server.Hostname,1}:{server.Port}\t| {server.info.Name}\n";
                }
                else
                {
                    serverInfo += $"{server.Hostname}:{server.Port} \t| Not responding\n";
                }
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

    internal WebsocketInstance AddSocket(WebSocket webSocket, TaskCompletionSource socketFinishedTcs)
    {
        var ws = new WebsocketInstance(webSocket, socketFinishedTcs);
        lock (webSocketListLock)
        {
            webSocketList.Add(ws);
        }
        return ws;
    }

    internal PlayerInfo? GetPlayerInfo(int id)
    {
        lock (watchedServersLock)
        {
            var record = watchedServers.FirstOrDefault((s) => s!.ID == id, null);
            if (record == null) return null;
            return record.PlayerInfo;
        }
    }

    internal ServerRules? GetServerRules(int id)
    {
        lock (watchedServersLock)
        {
            var record = watchedServers.FirstOrDefault((s) => s!.ID == id, null);
            if (record == null) return null;
            return record.Rules;
        }
    }
}


