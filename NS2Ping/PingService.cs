namespace NS2Ping;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;


public class PingService : BackgroundService
{
    public class WebsocketInstance
    {
        public WebSocket webSocket { get; }
        public TaskCompletionSource socketFinishedTcs { get; }
        public DateTime lastKeepAlive { get; set; }

        // Serialize sends per socket to avoid concurrent SendAsync
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public WebsocketInstance(WebSocket webSocket, TaskCompletionSource socketFinishedTcs)
        {
            this.webSocket = webSocket;
            this.socketFinishedTcs = socketFinishedTcs;
            this.lastKeepAlive = DateTime.UtcNow;
        }

        public async Task Read(CancellationToken ct)
        {
            var buffer = new byte[1024];
            try
            {
                while (!ct.IsCancellationRequested && webSocket.State == WebSocketState.Open)
                {
                    var receiveResult = await webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), ct);

                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                        socketFinishedTcs.TrySetResult();
                        break;
                    }

                    // Any message counts as keep-alive
                    lastKeepAlive = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) { /* normal on shutdown */ }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "WebSocket receive error");
                socketFinishedTcs.TrySetResult();
            }
        }

        public async Task SendCloseAsync()
        {
            if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
            {
                try { await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
                catch { /* ignore */ }
            }
        }

        public async Task<bool> TrySendAsync(ArraySegment<byte> payload, CancellationToken ct)
        {
            await _sendLock.WaitAsync(ct);
            try
            {
                if (webSocket.State != WebSocketState.Open) return false;
                await webSocket.SendAsync(payload, WebSocketMessageType.Text, true, ct);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "WebSocket send failed");
                return false;
            }
            finally
            {
                _sendLock.Release();
            }
        }
    }

    public static ILogger<PingService> _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<PingService>.Instance;

    public UdpClient client;
    public IPEndPoint ep;
    private List<ServerRecord> watchedServers;
    private readonly object watchedServersLock = new object();
    public bool running = false;
    public bool sleeping = false;
    private DateTime lastRequest = DateTime.UtcNow;
    public TimeSpan SleepTimeout = TimeSpan.FromSeconds(20);
    public TimeSpan WebsocketTimeout = TimeSpan.FromSeconds(60);
    private CancellationTokenSource sleepToken = new CancellationTokenSource();
    private MasterServerQueryWeb masterQuery;
    private static int printCounter = 0;
    private List<WebsocketInstance> webSocketList = new List<WebsocketInstance>();
    private readonly object webSocketListLock = new object();

    // Recreate the UDP socket after wake (Linux often needs this)
    private readonly bool _recreateSocketOnWake = true || OperatingSystem.IsLinux();
    private volatile bool _recreateRequested = false;

    public PingService(ILogger<PingService> logger)
    {
        _logger = logger;
        watchedServers = new List<ServerRecord>();
        ep = new IPEndPoint(IPAddress.Any, 0);
        client = new UdpClient(ep);
        masterQuery = new MasterServerQueryWeb();
    }

    private void Init()
    {
        watchedServers = new List<ServerRecord>();
        ep = new IPEndPoint(IPAddress.Any, 0);
        client = new UdpClient(ep);
        HandleNewServers();
        // masterQuery = new MasterServerQueryWeb();
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

            // Request socket recreation on wake on platforms that need it
            if (_recreateSocketOnWake)
            {
                _recreateRequested = true;
            }

            // Reset/replace sleep token
            sleepToken = new CancellationTokenSource();

            // Cancel and dispose the old token source to avoid leaks
            try { activeToken.Cancel(); } catch { /* ignore */ }
            activeToken.Dispose();
        }
        lastRequest = DateTime.UtcNow;
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
            HandleNewServers();
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
                    if (gameaddr.ToString().Equals("157.90.129.121:28315") || gameaddr.ToString().Equals("185.83.152.202:28315"))
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
                    int delayTime = 500;
                    if (DateTime.UtcNow - lastRequest > SleepTimeout)
                    {
                        delayTime = 1000 * 60 * 60 * 24;
                        sleeping = true;
                        _logger.LogInformation("PingService going to sleep");
                    }

                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(sleepToken.Token, stoppingToken);
                    var delayTask = Task.Delay(delayTime, linked.Token);

                    if (receiveTask.IsFaulted)
                    {
                        _logger.LogWarning("ReceiveTask faulted!");
                        _logger.LogWarning(receiveTask.Exception?.ToString());
                        // restart receive loop on fault
                        receiveTask = client.ReceiveAsync();
                    }

                    while (await Task.WhenAny(delayTask, receiveTask) == receiveTask)
                    {
                        try
                        {
                            var result = await receiveTask; // throws if faulted/canceled
                            NetworkStats.ReceivedBytes(result.Buffer.Length);
                            _logger.LogTrace($"Handling UDP response from {result.RemoteEndPoint}");
                            lock (watchedServersLock)
                            {
                                var server = watchedServers.SingleOrDefault(v => IPEndPoint.Equals(v!.EndPoint, result.RemoteEndPoint), null);
                                server?.HandleResponse(result.Buffer, client);
                            }
                        }
                        catch (ObjectDisposedException ex)
                        {
                            _logger.LogDebug(ex, "Socket disposed during receive");
                            throw; // handled by outer catch to re-init if needed
                        }
                        catch (SocketException ex)
                        {
                            _logger.LogDebug(ex, "Socket exception during receive");
                            throw;
                        }
                        finally
                        {
                            receiveTask = client.ReceiveAsync();
                        }
                    }

                    if (delayTask.IsCanceled)
                    {
                        _logger.LogDebug("DelayTask was cancelled");
                        if (_recreateRequested)
                        {
                            _logger.LogInformation("Recreating UDP socket after wake");
                            var oldClient = client;
                            client = new UdpClient(ep);
                            try { oldClient.Dispose(); } catch { /* ignore */ }
                            receiveTask = client.ReceiveAsync();
                            _recreateRequested = false;
                        }
                    }

                    await UpdateWebsocketsAsync(stoppingToken);

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
                    try { await Task.Delay(3000, stoppingToken); } catch (OperationCanceledException) { }

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
                            try { Task.Delay(3000, stoppingToken).Wait(stoppingToken); } catch { }
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
                _logger.LogInformation("Reinitializing PingService");

                // Close all websockets outside the lock and await
                List<WebsocketInstance> toClose;
                lock (webSocketListLock)
                {
                    toClose = webSocketList.ToList();
                    webSocketList.Clear();
                }
                try
                {
                    await Task.WhenAll(toClose.Select(s => s.SendCloseAsync()));
                }
                catch { /* ignore */ }
                Init();
            }
        } while (allowNewRun);

        _logger.LogInformation("Exiting PingService");
        running = false;
    }

    private async Task UpdateWebsocketsAsync(CancellationToken ct)
    {
        List<WebsocketInstance> socketsCopy;
        lock (webSocketListLock)
        {
            // Clean closed/expired sockets
            var cleanupList = new List<WebsocketInstance>();
            var now = DateTime.UtcNow;
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
                    socket.socketFinishedTcs.TrySetResult();
                    cleanupList.Add(socket);
                }
            }
            foreach (var socket in cleanupList)
            {
                webSocketList.Remove(socket);
                _ = socket.SendCloseAsync(); // fire-and-forget close
            }

            if (webSocketList.Count == 0) return;
            socketsCopy = webSocketList.ToList();
        }

        var serverList = GetServerInfosWithChanges();
        if (serverList.Count == 0) return;

        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        var payload = JsonSerializer.Serialize(serverList, options);
        var bytes = Encoding.UTF8.GetBytes(payload);
        var arraySegment = new ArraySegment<byte>(bytes);

        // Send outside the lock, serialize per-socket sends
        var sendTasks = socketsCopy.Select(async s =>
        {
            var ok = await s.TrySendAsync(arraySegment, ct);
            if (!ok)
            {
                s.socketFinishedTcs.TrySetResult();
            }
        });
        await Task.WhenAll(sendTasks);
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
        _logger.LogInformation($"Network stats: \n {NetworkStats.GetInformation()}");
        NetworkStats.Reset();
    }

    internal WebsocketInstance AddSocket(WebSocket webSocket, TaskCompletionSource socketFinishedTcs)
    {
        StopSleep();
        var ws = new WebsocketInstance(webSocket, socketFinishedTcs);
        lock (webSocketListLock)
        {
            webSocketList.Add(ws);
        }
        return ws;
    }

    internal List<PlayerInfo.PlayerInfoEntry>? GetPlayerInfo(int id)
    {
        lock (watchedServersLock)
        {
            var record = watchedServers.FirstOrDefault((s) => s!.ID == id && s.PlayerInfo != null, null);
            if (record == null) return null;
            return record.PlayerInfo!.Entries;
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


