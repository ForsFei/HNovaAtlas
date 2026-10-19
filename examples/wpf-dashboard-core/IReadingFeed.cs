namespace WpfDashboardCore;

public interface IReadingFeed
{
    Task<IReadOnlyList<ReadingSnapshot>> ReadAsync(bool simulateFailure, CancellationToken cancellationToken);
}
