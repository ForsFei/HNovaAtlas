namespace WpfAsyncReadings;

public interface IReadingSource
{
    Task<int> ReadAsync(bool simulateFailure, CancellationToken cancellationToken);
}
