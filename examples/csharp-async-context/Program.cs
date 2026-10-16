using System.Collections.Concurrent;

var context = new QueueingContext();
var capturedGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Task<bool> captured = StartInContext(context, () => ObserveContextAsync(capturedGate.Task, true, context));
Check(!captured.IsCompleted, "the first await must be pending");
capturedGate.SetResult();
await context.RunNextAsync();
Check(await captured, "default await resumes through the captured context");
Check(context.PostCount == 1, "one continuation was posted");

var freeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Task<bool> free = StartInContext(context, () => ObserveContextAsync(freeGate.Task, false, context));
freeGate.SetResult();
Check(!await free.WaitAsync(TimeSpan.FromSeconds(5)), "ConfigureAwait(false) skips context capture");
Check(context.PostCount == 1, "no extra continuation was posted");

Task<bool> alreadyDone = StartInContext(context, () => ObserveContextAsync(Task.CompletedTask, false, context));
Check(await alreadyDone, "an already-completed await may continue inline in the original context");

var trace = new AsyncLocal<string?> { Value = "request-42" };
var traceGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Task<string?> flowed = StartInContext(context, () => ReadTraceAsync(traceGate.Task, trace));
traceGate.SetResult();
Check(await flowed.WaitAsync(TimeSpan.FromSeconds(5)) == "request-42", "AsyncLocal flows independently");
Check(context.PostCount == 1, "ExecutionContext flow does not require a context post");

Console.WriteLine("上下文恢复、跳过回送、同步完成与 AsyncLocal 断言全部通过。");

static Task<T> StartInContext<T>(SynchronizationContext context, Func<Task<T>> action)
{
    SynchronizationContext? previous = SynchronizationContext.Current;
    SynchronizationContext.SetSynchronizationContext(context);
    try { return action(); }
    finally { SynchronizationContext.SetSynchronizationContext(previous); }
}

static async Task<bool> ObserveContextAsync(Task gate, bool capture, SynchronizationContext expected)
{
    if (capture) await gate;
    else await gate.ConfigureAwait(false);
    return ReferenceEquals(SynchronizationContext.Current, expected);
}

static async Task<string?> ReadTraceAsync(Task gate, AsyncLocal<string?> trace)
{
    await gate.ConfigureAwait(false);
    return trace.Value;
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class QueueingContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly SemaphoreSlim _posted = new(0);
    private int _postCount;
    public int PostCount => Volatile.Read(ref _postCount);

    public override void Post(SendOrPostCallback callback, object? state)
    {
        _queue.Enqueue((callback, state));
        Interlocked.Increment(ref _postCount);
        _posted.Release();
    }

    public async Task RunNextAsync()
    {
        if (!await _posted.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            throw new TimeoutException("No continuation was posted.");
        if (!_queue.TryDequeue(out var work)) throw new InvalidOperationException("Posted work is missing.");

        SynchronizationContext? previous = Current;
        SetSynchronizationContext(this);
        try { work.Callback(work.State); }
        finally { SetSynchronizationContext(previous); }
    }
}
