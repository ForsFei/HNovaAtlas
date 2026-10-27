using System.Text;
using SerialFraming;

int assertions = 0;
byte[] wire = Encoding.ASCII.GetBytes("TEMP=253\r\nTEMP=-15\r\n");
for (int split = 0; split <= wire.Length; split++)
{
    var decoder = new CrlfFrameDecoder();
    string[] frames = [.. decoder.Feed(wire.AsSpan(0, split)), .. decoder.Feed(wire.AsSpan(split))];
    Check(frames.SequenceEqual(new[] { "TEMP=253", "TEMP=-15" }), $"两段切分 {split}");
    Check(decoder.BufferedBytes == 0, "完整消费后无残留");
}

var single = new CrlfFrameDecoder();
var oneAtATime = new List<string>();
foreach (byte value in wire) oneAtATime.AddRange(single.Feed(new[] { value }));
Check(oneAtATime.Count == 2, "逐字节仍输出两帧");
Check(single.Feed([]).Count == 0, "空输入不产生帧");
var bounded = new CrlfFrameDecoder(3);
Check(bounded.Feed("ABC\r"u8).Count == 0 && bounded.BufferedBytes == 4, "允许上限载荷和分段 CR");
Check(bounded.Feed("\n"u8).Single() == "ABC", "LF 到达才输出");
foreach (byte[] invalid in new byte[][] { "ABCD"u8.ToArray(), "A\n"u8.ToArray(),
    "A\rB"u8.ToArray(), "\r\n"u8.ToArray(), [0xFF] })
{
    try { bounded.Feed(invalid); throw new InvalidOperationException("异常帧未被拒绝"); }
    catch (FormatException) { Check(bounded.BufferedBytes == 0, "异常清除半帧"); }
}
bounded.Feed("OLD\r"u8);
bounded.Reset();
Check(bounded.Feed("NEW\r\n"u8).Single() == "NEW", "新会话不继承旧半帧");

await SuccessfulSession();
await IdleCancellation();
await CancellationDuringRead();
await CancelledBeforeStart();
await FailedHandoff();
await FailureSession(new ProbePort { FailOpen = true }, typeof(IOException));
await FailureSession(new ProbePort { FailWrite = true }, typeof(TimeoutException));
await FailureSession(new ProbePort { FailRead = true }, typeof(IOException));
await FailureSession(new ProbePort("TEMP=x\r\n"), typeof(FormatException));
await FailureSession(new ProbePort("TEMP=253\r\nTEMP=254\r\n"), typeof(FormatException));
await FailureSession(new ProbePort("TEMP=253\r\nT"), typeof(FormatException));
await FailureSession(new ProbePort(), typeof(TimeoutException)); // Actual 2 s response deadline.
Console.WriteLine($"PASS: {assertions} assertions; framing + session lifecycle. No COM port opened.");

void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    assertions++;
}

async Task SuccessfulSession()
{
    var port = new ProbePort("TE", "MP=2", "53\r", "\n");
    var session = new SerialSession();
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    int received = 0;
    await session.RunAsync(() => port, update =>
    {
        if (update.Kind == UpdateKind.Connected)
        {
            Check(session.TryRequest(), "允许首个请求");
            Check(!session.TryRequest(), "拒绝重叠请求");
        }
        if (update.Kind == UpdateKind.Reading)
        {
            Check(update.Temperature == 25.3m, "组帧后才解析温度");
            received++;
            stop.Cancel();
        }
        return Task.CompletedTask;
    }, stop.Token);
    Check(received == 1 && port.Writes == 1 && port.Opened && port.Disposed, "正常停止且释放");
}

async Task IdleCancellation()
{
    var port = new ProbePort();
    var session = new SerialSession();
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await session.RunAsync(() => port, _ => { stop.Cancel(); return Task.CompletedTask; }, stop.Token);
    Check(port.Disposed && port.Writes == 0, "空闲取消不发送请求且释放");
}

async Task CancellationDuringRead()
{
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var port = new ProbePort { OnRead = stop.Cancel };
    await new SerialSession().RunAsync(() => port, _ => Task.CompletedTask, stop.Token);
    Check(port.Reads == 1 && port.Disposed, "同步读期间取消，返回后退出并释放");
}

async Task CancelledBeforeStart()
{
    using var stop = new CancellationTokenSource();
    stop.Cancel();
    bool created = false;
    try
    {
        await new SerialSession().RunAsync(() => { created = true; return new ProbePort(); },
            _ => Task.CompletedTask, stop.Token);
        throw new InvalidOperationException("预期提前取消");
    }
    catch (OperationCanceledException) { Check(!created, "提前取消不创建或打开端口"); }
}

async Task FailedHandoff()
{
    var port = new ProbePort();
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    try
    {
        await new SerialSession().RunAsync(() => port,
            _ => Task.FromException(new InvalidOperationException("injected UI handoff failure")), stop.Token);
        throw new Exception("预期交接失败");
    }
    catch (InvalidOperationException) { Check(port.Disposed, "UI 交接失败同样释放端口"); }
}

async Task FailureSession(ProbePort port, Type expected)
{
    var session = new SerialSession();
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    try
    {
        await session.RunAsync(() => port, update =>
        {
            if (update.Kind == UpdateKind.Connected) session.TryRequest();
            return Task.CompletedTask;
        }, stop.Token);
        throw new InvalidOperationException("预期会话失败");
    }
    catch (Exception error) when (error.GetType() == expected)
    {
        Check(port.Disposed, $"{expected.Name} 后释放，包括打开失败");
        Check(port.Writes <= 1, "失败后不自动重发");
    }
}

sealed class ProbePort(params string[] chunks) : IBytePort
{
    private readonly Queue<byte[]> _chunks = new(chunks.Select(Encoding.ASCII.GetBytes));
    public bool FailOpen { get; init; }
    public bool FailRead { get; init; }
    public bool FailWrite { get; init; }
    public Action? OnRead { get; init; }
    public bool Opened { get; private set; }
    public bool Disposed { get; private set; }
    public int Writes { get; private set; }
    public int Reads { get; private set; }

    public void Open()
    {
        if (FailOpen) throw new IOException("injected Open failure");
        Opened = true;
    }

    public void WriteLine(string text)
    {
        Writes++;
        if (FailWrite) throw new TimeoutException("injected Write timeout");
        if (text != "READ TEMP") throw new FormatException("unexpected request");
    }

    public int Read(byte[] buffer)
    {
        Reads++;
        OnRead?.Invoke();
        if (FailRead) throw new IOException("injected Read failure");
        if (Writes > 0 && _chunks.TryDequeue(out byte[]? chunk))
        {
            chunk.CopyTo(buffer, 0);
            return chunk.Length;
        }
        Thread.Sleep(2);
        throw new TimeoutException();
    }

    public void Dispose() => Disposed = true;
}
