using McpXLib;
using McpXLib.Enums;

namespace IndustrialMc;

internal static class McExamples
{
    public static McpX Connect(LoopbackMcDevice device, ushort timeoutMilliseconds = 800)
    {
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Pinned SDK uses host-endian BitConverter.");
        if (timeoutMilliseconds is < 500 or > 5000) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        return new McpX(device.Endpoint.Address.ToString(), device.Endpoint.Port,
            password: null, isAscii: false, isUdp: false, requestFrame: RequestFrame.E3,
            timeoutMilliseconds: timeoutMilliseconds, processorSeries: ProcessorSeries.Q, useMultiBlockAccess: false);
    }

    // Own the sync constructor AND all work off the UI thread. Do not race Dispose with requests.
    // 0.13.0 has no caller CancellationToken in async I/O: stop is checked between awaited calls.
    public static Task<T> RunAsync<T>(LoopbackMcDevice device, Func<McpX, CancellationToken, Task<T>> work,
        CancellationToken stop = default)
    {
        return Task.Run(async () =>
        {
            stop.ThrowIfCancellationRequested();
            using McpX plc = Connect(device);
            stop.ThrowIfCancellationRequested();
            return await work(plc, stop);
        });
    }

    public static async Task<T[]> ReadExactAsync<T>(McpX plc, Prefix prefix, string address, ushort count)
        where T : unmanaged
    {
        if (count is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(count));
        T[] raw = await plc.BatchReadAsync<T>(prefix, address, count);
        if (raw.Length != count) throw new InvalidDataException("Response element count differs from the demo contract.");
        return raw;
    }

    public static async Task WriteTargetAsync(McpX plc, short tenths, CancellationToken stop = default)
    {
        if (tenths is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(tenths));
        stop.ThrowIfCancellationRequested();
        await plc.WriteAsync<short>(Prefix.D, "110", tenths); // Exactly one attempt.
        // Do not overwrite this write's result with cancellation after an acknowledged response.
    }
}
