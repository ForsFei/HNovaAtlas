using System.Diagnostics;
using IndustrialSerialPort;

namespace SerialFraming;

public enum UpdateKind { Connected, Sent, Reading }
public sealed record SessionUpdate(UpdateKind Kind, string Message, decimal? Temperature = null);

public sealed class SerialSession
{
    private int _request;
    private int _started;

    // One queued/in-flight request maximum; no unbounded command queue.
    public bool TryRequest() => Interlocked.CompareExchange(ref _request, 1, 0) == 0;

    public Task RunAsync(Func<IBytePort> createPort, Func<SessionUpdate, Task> report,
        CancellationToken stop)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("每次连接创建一个新会话。");

        // All port access belongs to this logical worker, including Open/Dispose.
        return Task.Run(async () =>
        {
            stop.ThrowIfCancellationRequested();
            using IBytePort port = createPort();
            port.Open();
            await report(new(UpdateKind.Connected, "已连接；可读取一次温度。"));
            var decoder = new CrlfFrameDecoder();
            byte[] buffer = new byte[256];
            bool awaitingReply = false;
            long sentAt = 0;

            while (!stop.IsCancellationRequested)
            {
                if (!awaitingReply && Volatile.Read(ref _request) == 1)
                {
                    sentAt = Stopwatch.GetTimestamp();
                    port.WriteLine(TemperatureProtocol.Request);
                    awaitingReply = true;
                    await report(new(UpdateKind.Sent, "TX: READ TEMP\\r\\n（等待完整响应）"));
                }

                CheckDeadline();
                int count;
                try { count = port.Read(buffer); }
                catch (TimeoutException) { continue; } // Empty read window, not a frame boundary.
                stop.ThrowIfCancellationRequested();
                CheckDeadline();
                if (count > 0 && !awaitingReply)
                    throw new FormatException("未请求时收到字节，无法可靠匹配，终止会话。");

                foreach (string frame in decoder.Feed(buffer.AsSpan(0, count)))
                {
                    // This demo contract has no unsolicited messages or response IDs.
                    if (!awaitingReply) throw new FormatException("收到未请求或多余响应，终止会话。");
                    decimal value = TemperatureProtocol.ParseResponse(frame);
                    awaitingReply = false;
                    Interlocked.Exchange(ref _request, 0);
                    await report(new(UpdateKind.Reading, $"RX: {frame}", value));
                }
                if (!awaitingReply && decoder.BufferedBytes != 0)
                    throw new FormatException("响应之后还有多余半帧，终止会话。");
            }

            void CheckDeadline()
            {
                if (awaitingReply && Stopwatch.GetElapsedTime(sentAt) >= TimeSpan.FromSeconds(2))
                    throw new TimeoutException("响应超过 2 秒；关闭会话，不自动重发或匹配迟到响应。");
            }
        });
    }
}
