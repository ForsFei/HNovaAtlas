using System.Diagnostics;
using IndustrialModbus;
using IndustrialModbusMonitor;

int checks = 0;
foreach ((RegisterOrder order, ushort first, ushort second) in new[]
{
    (RegisterOrder.ABCD, (ushort)0x41C8, (ushort)0), (RegisterOrder.BADC, (ushort)0xC841, (ushort)0),
    (RegisterOrder.CDAB, (ushort)0, (ushort)0x41C8), (RegisterOrder.DCBA, (ushort)0, (ushort)0xC841),
}) Check(ModbusValueCodec.Float32(first, second, order) == 25f, $"{order} 黄金值25");
Check(ModbusValueCodec.UInt32(1, 2, RegisterOrder.ABCD) == 65538, "UInt32");
Check(ModbusValueCodec.Int32(0xFFFF, 0xFFFE, RegisterOrder.ABCD) == -2, "Int32 补码");
foreach (ushort raw in new ushort[] { 0x7FC0, 0x7F80, 0xFF80 })
{
    try { ModbusValueCodec.Float32(raw, 0, RegisterOrder.ABCD); throw new Exception("非有限数未拒绝"); }
    catch (FormatException) { checks++; }
}
Check(ModbusValueCodec.Ascii(new ushort[] { 0x4154, 0x4C41, 0x5330, 0x3100 }) == "ATLAS01", "ASCII 高字节先取");
Check(ModbusValueCodec.Ascii(new ushort[] { 0x4120, 0x2020 }) == "A", "去末尾空格填充");
foreach (ushort[] bad in new ushort[][] { [0x4100, 0x4243], [0x8000] })
{
    try { ModbusValueCodec.Ascii(bad); throw new Exception("非法字符串未拒绝"); }
    catch (FormatException) { checks++; }
}
Check(ModbusValueCodec.Bit(0x8001, 0) && ModbusValueCodec.Bit(0x8001, 15) && !ModbusValueCodec.Bit(0x8001, 14), "标志位0/15");
try { ModbusValueCodec.Bit(0, 16); throw new Exception("非法位未拒绝"); }
catch (ArgumentOutOfRangeException) { checks++; }
foreach ((string text, ushort raw) in new[] { ("0", (ushort)0), ("100", (ushort)1000), ("30.0", (ushort)300), ("0.1", (ushort)1) })
    Check(ModbusValueCodec.TrySetpoint(text, out ushort actual, out _) && actual == raw, "目标值精确缩放");
foreach (string text in new[] { "100.1", "-0.1", "30.05", "NaN", "1,5", "1e1", "", " 1", "1 " })
    Check(!ModbusValueCodec.TrySetpoint(text, out _, out _), "拒绝越界/隐式四舍五入/文化歧义");

await WriteCase(DeviceFault.None, WriteState.Verified, 30m, 1);
await WriteCase(DeviceFault.RejectWrites, WriteState.Rejected, 25m, 0);
await WriteCase(DeviceFault.AdjustSetpoint, WriteState.Mismatch, 29.9m, 1);
await WriteCase(DeviceFault.ApplyWriteThenDropOnce, WriteState.Uncertain, 30m, 1);
await QueuedStop();
await QueueExpiry();
await StaleAtSend();
await StopDuringWrite();
await ReadFailure();
Console.WriteLine($"PASS: {checks} checks; codecs, poll/write ordering, unknown outcomes and cancellation. Loopback only.");

void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
}

async Task Eventually(Func<bool> condition, string message)
{
    long start = Stopwatch.GetTimestamp();
    while (!condition())
    {
        if (Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(6)) throw new TimeoutException(message);
        await Task.Delay(5);
    }
}

async Task WriteCase(DeviceFault fault, WriteState expected, decimal target, int writes)
{
    await using var device = new LoopbackModbusDevice(fault);
    var monitor = new ModbusMonitor(TimeSpan.FromMilliseconds(80));
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    Check(!monitor.TryQueueTarget("30", out _), "启动前禁止写入");
    Task run = monitor.RunAsync(device.Endpoint, stop.Token);
    try
    {
        await Eventually(() => monitor.Snapshot.Sample is not null, "首个采样");
        Check(monitor.Snapshot.Sample!.Temperature == -12.3m && monitor.Snapshot.Sample.FloatValue == 25f, "UI业务采样契约");
        Check(monitor.TryQueueTarget("30.0", out _), "允许排入单次目标写入");
        Check(!monitor.TryQueueTarget("40", out _), "排队/在途时拒绝第二次写入");
        await Eventually(() => monitor.Snapshot.Write?.State == expected && !monitor.Snapshot.WriteBusy
            && monitor.Snapshot.Sample?.Target == target, $"{fault}: {expected}");
        Check(device.Writes == writes, $"{fault}: 实际修改次数");
        if (expected == WriteState.Verified) Check(monitor.Snapshot.Write!.ReadBack == 300, "写入回执之后再读回");
        if (expected == WriteState.Uncertain)
        {
            int requests = device.Requests;
            await Eventually(() => device.Requests >= requests + 6, "等两轮读取恢复");
            Check(device.Writes == 1 && monitor.Snapshot.Write?.State == WriteState.Uncertain, "恢复读取没有自动重写，也不猜测旧写入已验证");
        }
    }
    finally { stop.Cancel(); await run; }
    Check(monitor.Snapshot.State == MonitorState.Stopped && monitor.Snapshot.Sample is null, "停止使采样失效");
    Check(!monitor.TryQueueTarget("30", out _), "停止之后拒绝新命令");
}

async Task QueuedStop()
{
    await using var device = new LoopbackModbusDevice();
    var monitor = new ModbusMonitor(TimeSpan.FromSeconds(2));
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    Task run = monitor.RunAsync(device.Endpoint, stop.Token);
    try
    {
        await Eventually(() => monitor.Snapshot.Sample is not null, "首次采样");
        Check(monitor.TryQueueTarget("30", out _), "排队但尚未取走");
    }
    finally { stop.Cancel(); await run; }
    Check(monitor.Snapshot.Write?.State == WriteState.NotSent && device.Writes == 0, "取消尚未发送的命令");
}

async Task QueueExpiry()
{
    await using var device = new LoopbackModbusDevice();
    var monitor = new ModbusMonitor(TimeSpan.FromMilliseconds(200), queueLifetime: TimeSpan.FromMilliseconds(1));
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    Task run = monitor.RunAsync(device.Endpoint, stop.Token);
    try
    {
        await Eventually(() => monitor.Snapshot.Sample is not null, "首次采样");
        Check(monitor.TryQueueTarget("30", out _), "接受待过期命令");
        await Eventually(() => monitor.Snapshot.Write?.State == WriteState.NotSent, "队列过期");
        Check(device.Writes == 0, "过期命令没有延迟执行");
    }
    finally { stop.Cancel(); await run; }
}

async Task StopDuringWrite()
{
    await using var device = new LoopbackModbusDevice(DeviceFault.ApplyWriteThenSilence);
    var monitor = new ModbusMonitor(TimeSpan.FromMilliseconds(80));
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    Task run = monitor.RunAsync(device.Endpoint, stop.Token);
    try
    {
        await Eventually(() => monitor.Snapshot.Sample is not null, "首次采样");
        Check(monitor.TryQueueTarget("30", out _), "排队即将发送的命令");
        await Eventually(() => device.Writes == 1, "设备已经修改，但没有回执");
    }
    finally { stop.Cancel(); await run; }
    Check(monitor.Snapshot.Write?.State == WriteState.Uncertain && device.Writes == 1, "取消不能把已尝试的写入说成未发送");
}

async Task StaleAtSend()
{
    await using var device = new LoopbackModbusDevice();
    var monitor = new ModbusMonitor(TimeSpan.FromMilliseconds(3300), queueLifetime: TimeSpan.FromSeconds(20));
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    Task run = monitor.RunAsync(device.Endpoint, stop.Token);
    try
    {
        await Eventually(() => monitor.Snapshot.Sample is not null, "首次采样");
        Check(monitor.TryQueueTarget("30", out _), "新鲜时接受命令");
        await Eventually(() => monitor.Snapshot.Write?.State == WriteState.NotSent, "发送前再次检查采样新鲜度");
        Check(device.Writes == 0, "命令本身未过期，但采样已过期仍不写入");
    }
    finally { stop.Cancel(); await run; }
}

async Task ReadFailure()
{
    await using var device = new LoopbackModbusDevice(DeviceFault.Silent);
    var monitor = new ModbusMonitor(TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(150));
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    Task run = monitor.RunAsync(device.Endpoint, stop.Token);
    try { await run.WaitAsync(TimeSpan.FromSeconds(6)); }
    finally { if (!run.IsCompleted) { stop.Cancel(); await run; } }
    Check(monitor.Snapshot.State == MonitorState.Faulted && monitor.Snapshot.ReadFailures == 3, "连续读取失败达到上限停止");
    Check(monitor.Snapshot.Sample is null && !monitor.TryQueueTarget("30", out _), "失败时无旧有效值且不能写入");
}
