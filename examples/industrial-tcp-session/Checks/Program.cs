using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using IndustrialNetworking;
using IndustrialTcpSession;

int assertions = 0;
var fast = new MonitorSettings
{
    ConnectTimeout = TimeSpan.FromMilliseconds(300),
    RequestTimeout = TimeSpan.FromMilliseconds(180),
    PollInterval = TimeSpan.FromMilliseconds(80),
    RetryBaseDelay = TimeSpan.FromMilliseconds(80),
    RetryMaxDelay = TimeSpan.FromMilliseconds(160),
    MaxConsecutiveFailures = 2,
};
var defaults = new MonitorSettings();
foreach ((int failures, int milliseconds) in new[] { (1, 250), (2, 500), (3, 1000), (4, 2000), (99, 2000) })
    Check(defaults.RetryDelay(failures).TotalMilliseconds == milliseconds, "退避递增并封顶");
try { new TcpMonitor(fast with { MaxConsecutiveFailures = 0 }); throw new Exception("无效参数未拒绝"); }
catch (ArgumentOutOfRangeException) { assertions++; }
string id = WireProtocol.NewId();
Check(SessionProtocol.ParseRequest($"PING {id}") == (RequestKind.Ping, id), "PING 请求解析");
Check(SessionProtocol.ParseResponse($"PONG {id}").Kind == RequestKind.Ping, "PONG 不是温度");

await PersistentRequests();
await FilterReplies(DemoScenario.StaleReplies);
await FilterReplies(DemoScenario.DuplicateReplies);
await Recovery(DemoScenario.DropOnce);
await Recovery(DemoScenario.PartialOnce);
await IdleClose();
await Terminal(DemoScenario.SilentPing, 2);
await Terminal(DemoScenario.SilentTemperature, 2);
await Terminal(DemoScenario.Malformed, 1);
await Terminal(DemoScenario.WrongKind, 1);
await StopDuringRetry();
await StopDuringRequest();
await PreCancelled();
await UnavailableEndpoint();
Console.WriteLine($"PASS: {assertions} assertions; persistent TCP, heartbeat, recovery, cancellation. Loopback only.");

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    assertions++;
}

async Task Eventually(Func<bool> condition, string message)
{
    long start = Stopwatch.GetTimestamp();
    while (!condition())
    {
        if (Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(5)) throw new TimeoutException(message);
        await Task.Delay(5);
    }
}

async Task PersistentRequests()
{
    await using var device = new LoopbackDevice();
    using var client = new TcpClient();
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await client.ConnectAsync(device.Endpoint.Address, device.Endpoint.Port, stop.Token);
    var connection = new TcpConnection(client);
    try
    {
        Task<SessionReply> ping = connection.RequestAsync(RequestKind.Ping, TimeSpan.FromSeconds(1), stop.Token);
        Task<SessionReply> read = connection.RequestAsync(RequestKind.Temperature, TimeSpan.FromSeconds(1), stop.Token);
        Check((await ping).Kind == RequestKind.Ping, "并发调用由发送门串行化");
        Check((await read).Temperature == 25.3m, "同连接温度请求");
        Check((await connection.RequestAsync(RequestKind.Ping, TimeSpan.FromSeconds(1), stop.Token)).Id != (await ping).Id,
            "每个请求使用新编号");
        Check(device.Connections == 1 && device.Requests == 3, "多个请求复用同一连接");
    }
    finally { await connection.DisposeAsync(); }
    Check(connection.Completion.IsCompleted, "释放时接收任务已经退出");
}

async Task FilterReplies(DemoScenario scenario)
{
    await using var device = new LoopbackDevice(scenario);
    using var client = new TcpClient();
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await client.ConnectAsync(device.Endpoint.Address, device.Endpoint.Port, stop.Token);
    await using var connection = new TcpConnection(client);
    for (int index = 0; index < 3; index++)
        Check((await connection.RequestAsync(RequestKind.Temperature, TimeSpan.FromSeconds(1), stop.Token)).Temperature == 25.3m,
            $"{scenario} 不误配下一条响应");
    await Eventually(() => connection.IgnoredReplies == 3, "等忽略计数更新");
    Check(device.Connections == 1, $"{scenario} 不造成无谓重连");
}

async Task Recovery(DemoScenario scenario)
{
    await using var device = new LoopbackDevice(scenario);
    var monitor = new TcpMonitor(fast);
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(6));
    Task run = monitor.RunAsync(device.Endpoint, stop.Token);
    try
    {
        await Eventually(() => monitor.Snapshot.State == LinkState.Retrying, $"{scenario} 进入恢复等待");
        Check(monitor.Snapshot.Temperature is null && monitor.Snapshot.SampleTimestamp is null, "恢复等待立即使数据失效");
        await Eventually(() => monitor.Snapshot.State == LinkState.Online && monitor.Snapshot.Generation >= 2,
            $"{scenario} 恢复成功");
        MonitorSnapshot snapshot = monitor.Snapshot;
        Check(snapshot.Temperature == 25.3m && snapshot.SampleAge < TimeSpan.FromSeconds(1), "新会话得到新的有效采样");
        Check(snapshot.ConsecutiveFailures == 0 && device.Connections == 2, "完整健康周期后清连续失败计数");
    }
    finally { stop.Cancel(); await run; }
    Check(monitor.Snapshot.State == LinkState.Stopped && monitor.Snapshot.Temperature is null, "停止清读数");
}

async Task IdleClose()
{
    await using var device = new LoopbackDevice(DemoScenario.IdleCloseOnce);
    var monitor = new TcpMonitor(fast with { PollInterval = TimeSpan.FromSeconds(2) });
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(6));
    Task run = monitor.RunAsync(device.Endpoint, stop.Token);
    try
    {
        await Eventually(() => monitor.Snapshot.State == LinkState.Online, "等待首次健康采样");
        long start = Stopwatch.GetTimestamp();
        await Eventually(() => monitor.Snapshot.State == LinkState.Retrying, "空闲期间观察 EOF");
        Check(Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(1.5), "不必等下一个 2 秒轮询才能检测 EOF");
    }
    finally { stop.Cancel(); await run; }
}

async Task Terminal(DemoScenario scenario, int expectedConnections)
{
    await using var device = new LoopbackDevice(scenario);
    var monitor = new TcpMonitor(fast);
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(6));
    Task run = monitor.RunAsync(device.Endpoint, stop.Token);
    try { await run.WaitAsync(TimeSpan.FromSeconds(5)); }
    finally { if (!run.IsCompleted) { stop.Cancel(); await run; } }
    MonitorSnapshot snapshot = monitor.Snapshot;
    Check(snapshot.State == LinkState.Faulted && snapshot.Temperature is null, $"{scenario} 终止且无有效数据");
    Check(snapshot.Attempt == expectedConnections && device.Connections == expectedConnections, $"{scenario} 恢复次数受限");
    if (scenario == DemoScenario.SilentPing) Check(device.Temperatures == 0, "PONG 缺失时不发送温度查询");
    if (scenario == DemoScenario.SilentTemperature) Check(snapshot.ConsecutiveFailures == 2, "仅心跳成功不清失败计数");
}

async Task StopDuringRetry()
{
    await using var device = new LoopbackDevice(DemoScenario.SilentPing);
    var monitor = new TcpMonitor(fast with { RetryBaseDelay = TimeSpan.FromSeconds(2), RetryMaxDelay = TimeSpan.FromSeconds(2) });
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(6));
    Task run = monitor.RunAsync(device.Endpoint, stop.Token);
    try { await Eventually(() => monitor.Snapshot.State == LinkState.Retrying, "等待退避"); }
    finally { stop.Cancel(); await run; }
    Check(monitor.Snapshot.State == LinkState.Stopped && device.Connections == 1, "退避期间取消不再重连");
}

async Task StopDuringRequest()
{
    await using var device = new LoopbackDevice(DemoScenario.SilentPing);
    var monitor = new TcpMonitor(fast);
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(6));
    Task run = monitor.RunAsync(device.Endpoint, stop.Token);
    try { await Eventually(() => device.Pings == 1, "等待挂起的心跳请求"); }
    finally { stop.Cancel(); await run; }
    Check(monitor.Snapshot.State == LinkState.Stopped && device.Connections == 1, "挂起请求取消，接收退出且不恢复");
}

async Task PreCancelled()
{
    var monitor = new TcpMonitor(fast);
    using var stop = new CancellationTokenSource();
    stop.Cancel();
    await monitor.RunAsync(new IPEndPoint(IPAddress.Loopback, 1), stop.Token);
    Check(monitor.Snapshot.State == LinkState.Stopped && monitor.Snapshot.Attempt == 0, "提前取消不建立连接");
}

async Task UnavailableEndpoint()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var endpoint = (IPEndPoint)listener.LocalEndpoint;
    listener.Stop();
    var monitor = new TcpMonitor(fast with { MaxConsecutiveFailures = 1 });
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await monitor.RunAsync(endpoint, stop.Token);
    Check(monitor.Snapshot.State == LinkState.Faulted && monitor.Snapshot.Generation == 0, "连接失败没有冒充已建立会话");
}
