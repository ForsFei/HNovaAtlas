using System.IO;

namespace WpfDashboardCore;

public sealed class SimulatedReadingFeed : IReadingFeed
{
    private int _batch;

    public async Task<IReadOnlyList<ReadingSnapshot>> ReadAsync(
        bool simulateFailure, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        if (simulateFailure) throw new IOException("模拟数据源不可用。");

        int batch = Interlocked.Increment(ref _batch);
        return Enumerable.Range(1, 24)
            .Select(i => new ReadingSnapshot(i, $"设备-{i:D2}", 20 + (i * 7 + batch * 3) % 70))
            .ToArray();
    }
}
