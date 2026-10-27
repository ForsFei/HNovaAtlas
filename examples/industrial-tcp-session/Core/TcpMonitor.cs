using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace IndustrialTcpSession;

public enum LinkState { Stopped, Connecting, Online, Retrying, Faulted }
public sealed record MonitorSnapshot(LinkState State, int Attempt, int Generation,
    decimal? Temperature, long? SampleTimestamp, int ConsecutiveFailures, int IgnoredReplies, string Message)
{
    public TimeSpan? SampleAge => SampleTimestamp is long stamp ? Stopwatch.GetElapsedTime(stamp) : null;
}

public sealed record MonitorSettings
{
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan RetryMaxDelay { get; init; } = TimeSpan.FromSeconds(2);
    public int MaxConsecutiveFailures { get; init; } = 4;

    public void Validate()
    {
        foreach (TimeSpan value in new[] { ConnectTimeout, RequestTimeout, PollInterval, RetryBaseDelay, RetryMaxDelay })
            if (value <= TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value));
        if (RetryMaxDelay < RetryBaseDelay || MaxConsecutiveFailures < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxConsecutiveFailures));
    }

    public TimeSpan RetryDelay(int failureCount) => TimeSpan.FromMilliseconds(Math.Min(
        RetryMaxDelay.TotalMilliseconds, RetryBaseDelay.TotalMilliseconds * Math.Pow(2, Math.Min(20, Math.Max(0, failureCount - 1)))));
}

public sealed class TcpMonitor
{
    private readonly MonitorSettings _settings;
    private int _started;
    private MonitorSnapshot _snapshot = new(LinkState.Stopped, 0, 0, null, null, 0, 0, "未启动。");
    public MonitorSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public TcpMonitor(MonitorSettings? settings = null)
    {
        _settings = settings ?? new();
        _settings.Validate();
    }

    public async Task RunAsync(IPEndPoint endpoint, CancellationToken stop)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("每次启动创建新的监控实例。");
        int failures = 0, attempt = 0, generation = 0;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                attempt++;
                Publish(LinkState.Connecting, "正在建立 TCP 连接，尚无有效读数。");
                try
                {
                    using var client = new TcpClient(endpoint.AddressFamily) { NoDelay = true };
                    using (var connect = CancellationTokenSource.CreateLinkedTokenSource(stop))
                    {
                        connect.CancelAfter(_settings.ConnectTimeout);
                        try { await client.ConnectAsync(endpoint.Address, endpoint.Port, connect.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
                        { throw new TimeoutException("建立连接超时。"); }
                    }
                    generation++;
                    await using var connection = new TcpConnection(client);
                    while (true)
                    {
                        stop.ThrowIfCancellationRequested();
                        await connection.RequestAsync(RequestKind.Ping, _settings.RequestTimeout, stop).ConfigureAwait(false);
                        // PONG proves protocol liveness only; it is not a fresh temperature.
                        SessionReply reply = await connection.RequestAsync(RequestKind.Temperature, _settings.RequestTimeout, stop).ConfigureAwait(false);
                        failures = 0; // Reset only after a complete healthy poll, not after Connect.
                        Publish(LinkState.Online, "心跳与温度响应有效。", reply.Temperature,
                            Stopwatch.GetTimestamp(), connection.IgnoredReplies);
                        await WaitForNextPollAsync(connection, stop).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (FormatException error)
                {
                    Publish(LinkState.Faulted, $"协议失败，不自动重连：{error.Message}");
                    return;
                }
                catch (Exception error) when (error is IOException or SocketException or TimeoutException)
                {
                    if (stop.IsCancellationRequested) break;
                    failures++;
                    if (failures >= _settings.MaxConsecutiveFailures)
                    {
                        Publish(LinkState.Faulted, $"连续失败达到上限：{error.GetType().Name}: {error.Message}");
                        return;
                    }
                    TimeSpan delay = _settings.RetryDelay(failures);
                    Publish(LinkState.Retrying, $"旧读数失效；{delay.TotalMilliseconds:0} ms 后重试：{error.GetType().Name}");
                    await Task.Delay(delay, stop).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error)
        {
            Publish(LinkState.Faulted, $"未预期失败，停止监控：{error.GetType().Name}: {error.Message}");
            throw;
        }
        finally { if (stop.IsCancellationRequested) Publish(LinkState.Stopped, "已停止，读数失效。"); }

        void Publish(LinkState state, string message, decimal? temperature = null, long? stamp = null, int ignored = 0) =>
            Volatile.Write(ref _snapshot, new(state, attempt, generation, temperature, stamp, failures, ignored, message));
    }

    private async Task WaitForNextPollAsync(TcpConnection connection, CancellationToken stop)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(stop);
        Task delay = Task.Delay(_settings.PollInterval, idle.Token);
        if (await Task.WhenAny(delay, connection.Completion).ConfigureAwait(false) == connection.Completion)
        {
            idle.Cancel();
            try { await delay.ConfigureAwait(false); }
            catch (OperationCanceledException) when (idle.IsCancellationRequested) { }
            await connection.Completion.ConfigureAwait(false); // Surface idle-time EOF immediately.
        }
        await delay.ConfigureAwait(false);
    }
}
