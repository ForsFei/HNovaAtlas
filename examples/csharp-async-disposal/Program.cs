var normalTrace = new List<string>();
var normalGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var owned = new TrackedLease(normalGate.Task, normalTrace);
Task normal = UseOwnedAsync(owned, normalTrace);
Check(!normal.IsCompleted && normalTrace.SequenceEqual(["body", "dispose-start"]),
    "await using waits for incomplete cleanup");
normalGate.SetResult();
await normal;
Check(owned.DisposeCalls == 1 && normalTrace.SequenceEqual(["body", "dispose-start", "dispose-end"]),
    "the scope completes only after disposal completes");

var failureTrace = new List<string>();
var failureGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var failingLease = new TrackedLease(failureGate.Task, failureTrace);
var expected = new InvalidOperationException("work failed");
Task failed = UseFailingAsync(failingLease, failureTrace, expected);
Check(!failed.IsCompleted && failureTrace.SequenceEqual(["body", "dispose-start"]),
    "a body fault still awaits cleanup before reaching the caller");
failureGate.SetResult();
try { await failed; throw new Exception("expected work failure"); }
catch (InvalidOperationException error) when (ReferenceEquals(error, expected)) { }
Check(failingLease.DisposeCalls == 1 && failureTrace[^1] == "dispose-end",
    "cleanup finished before the body fault was observed");

var borrowedTrace = new List<string>();
var borrowed = new TrackedLease(Task.CompletedTask, borrowedTrace);
UseBorrowed(borrowed, borrowedTrace);
Check(borrowed.DisposeCalls == 0, "a borrower does not dispose its caller's lease");
await borrowed.DisposeAsync(); // The owner closes its own resource.
Check(borrowed.DisposeCalls == 1 && borrowedTrace.SequenceEqual(["use", "dispose-start", "dispose-end"]),
    "the owner eventually disposes the borrowed resource");

Console.WriteLine("异步清理与资源所有权断言全部通过。");

static async Task UseOwnedAsync(TrackedLease lease, List<string> trace)
{
    await using (lease) { trace.Add("body"); }
}

static async Task UseFailingAsync(TrackedLease lease, List<string> trace, Exception failure)
{
    await using (lease)
    {
        trace.Add("body");
        throw failure;
    }
}

static void UseBorrowed(TrackedLease lease, List<string> trace)
{
    Check(lease.DisposeCalls == 0, "borrowed lease is open");
    trace.Add("use");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class TrackedLease(Task releaseGate, List<string> trace) : IAsyncDisposable
{
    private bool _disposed;
    public int DisposeCalls { get; private set; }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        DisposeCalls++;
        trace.Add("dispose-start");
        return new ValueTask(FinishAsync());
    }

    private async Task FinishAsync()
    {
        await releaseGate.ConfigureAwait(false);
        trace.Add("dispose-end");
    }
}
