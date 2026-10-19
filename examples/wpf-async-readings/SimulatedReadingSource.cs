using System.IO;

namespace WpfAsyncReadings;

public sealed class SimulatedReadingSource : IReadingSource
{
    public async Task<int> ReadAsync(bool simulateFailure, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        if (simulateFailure) throw new IOException("模拟连接中断");
        return 72;
    }
}
