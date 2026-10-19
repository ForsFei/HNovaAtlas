using System.Collections.Concurrent;
using System.Threading.Channels;

var counter = new Counter();
Task[] writers = Enumerable.Range(0, 4)
    .Select(_ => Task.Run(() =>
    {
        for (int i = 0; i < 5_000; i++) counter.Increment();
    }))
    .ToArray();
await Task.WhenAll(writers);
Require(counter.Value == 20_000, "lock 保护计数");

var counts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
Task[] updates = Enumerable.Range(0, 4)
    .Select(_ => Task.Run(() =>
    {
        for (int i = 0; i < 5_000; i++)
            counts.AddOrUpdate("pump-A", 1, (_, old) => old + 1);
    }))
    .ToArray();
await Task.WhenAll(updates);
Require(counts["pump-A"] == 20_000, "并发字典计数");

Channel<int> channel = Channel.CreateBounded<int>(new BoundedChannelOptions(2)
{
    FullMode = BoundedChannelFullMode.Wait
});
Task producer = ProduceAsync();
int sum = 0;
await foreach (int item in channel.Reader.ReadAllAsync()) sum += item;
await producer;
Require(sum == 15, "有界 Channel 消费");

using var gate = new SemaphoreSlim(2, 2);
int active = 0;
Task[] jobs = Enumerable.Range(0, 6).Select(ProcessAsync).ToArray();
await Task.WhenAll(jobs);

using var source = new CancellationTokenSource();
source.Cancel();
try
{
    await Task.Delay(10, source.Token);
    throw new InvalidOperationException("取消未生效");
}
catch (OperationCanceledException) when (source.IsCancellationRequested)
{
    Console.WriteLine("协作取消验证通过");
}

Console.WriteLine("并发示例验证通过：lock、ConcurrentDictionary、Channel、SemaphoreSlim");

async Task ProduceAsync()
{
    for (int i = 1; i <= 5; i++) await channel.Writer.WriteAsync(i);
    channel.Writer.TryComplete();
}

async Task ProcessAsync(int _)
{
    await gate.WaitAsync();
    try
    {
        int now = Interlocked.Increment(ref active);
        Require(now <= 2, "SemaphoreSlim 并发上限");
        await Task.Delay(5);
    }
    finally
    {
        Interlocked.Decrement(ref active);
        gate.Release();
    }
}

static void Require(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException($"验证失败：{label}");
}

public sealed class Counter
{
    private readonly object _gate = new();
    private int _value;

    public void Increment()
    {
        lock (_gate) { _value++; }
    }

    public int Value
    {
        get { lock (_gate) { return _value; } }
    }
}
