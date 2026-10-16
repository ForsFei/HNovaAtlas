using System.Runtime.CompilerServices;

var earlyTrace = new List<string>();
IAsyncEnumerable<int> pending = ReadAsync(earlyTrace);
Check(earlyTrace.Count == 0, "body starts on enumeration");

await foreach (int value in pending)
{
    Check(value == 0, "first item");
    break;
}
Check(earlyTrace.SequenceEqual(["open", "item:0", "close"]), "break disposes enumerator");

var cancelTrace = new List<string>();
using var source = new CancellationTokenSource();
int received = 0;
try
{
    await foreach (int value in ReadAsync(cancelTrace).WithCancellation(source.Token))
    {
        Check(value == 0, "cancellation after first item");
        received++;
        source.Cancel();
    }
    throw new Exception("expected cancellation");
}
catch (OperationCanceledException) when (source.IsCancellationRequested)
{
    Check(received == 1, "one item delivered");
}
Check(cancelTrace.SequenceEqual(["open", "item:0", "close"]), "cancellation disposes enumerator");

Console.WriteLine("异步流延迟、提前退出与取消断言全部通过");

static async IAsyncEnumerable<int> ReadAsync(
    List<string> trace, [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    await using var lease = new TrackedLease(trace);
    for (int value = 0; value < 3; value++)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield(); // 只制造可观察的异步暂停点，不代表真实 I/O
        cancellationToken.ThrowIfCancellationRequested();
        trace.Add($"item:{value}");
        yield return value;
    }
}

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"断言失败：{name}");
}

sealed class TrackedLease : IAsyncDisposable
{
    private readonly List<string> _trace;

    public TrackedLease(List<string> trace)
    {
        _trace = trace;
        _trace.Add("open");
    }

    public ValueTask DisposeAsync()
    {
        _trace.Add("close");
        return ValueTask.CompletedTask;
    }
}
