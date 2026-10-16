Task<int> reusable = Task.FromResult(7);
Check(await reusable == 7 && await reusable == 7, "Task supports repeated awaits");

var cache = new CachedReading();
ValueTask<int> pending = cache.ReadAsync();
Check(!pending.IsCompletedSuccessfully, "cache miss is pending");
cache.Complete(42);
Check(await pending == 42, "pending result"); // 只消费这份 ValueTask 一次

ValueTask<int> ready = cache.ReadAsync();
Check(ready.IsCompletedSuccessfully, "cache hit is ready");
Check(await ready == 42, "ready result"); // 新取得的 ValueTask，也只消费一次

ValueTask<int> toCompose = cache.ReadAsync();
Task<int> stable = toCompose.AsTask(); // 转换一次，之后只使用 Task
int[] results = await Task.WhenAll(stable, Task.FromResult(1));
Check(results.SequenceEqual([42, 1]), "Task.WhenAll composition");

Console.WriteLine("Task 与 ValueTask 消费契约断言全部通过");

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"断言失败：{name}");
}

sealed class CachedReading
{
    private readonly TaskCompletionSource<int> _pending =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int? _cached;

    public ValueTask<int> ReadAsync() => _cached is int value
        ? ValueTask.FromResult(value)
        : new ValueTask<int>(_pending.Task);

    public void Complete(int value)
    {
        _cached = value;
        _pending.SetResult(value);
    }
}
