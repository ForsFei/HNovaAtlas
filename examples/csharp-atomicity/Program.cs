using System.Threading;

var counter = new AtomicCounter();
Task[] workers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
{
    for (int i = 0; i < 5_000; i++) counter.Increment();
})).ToArray();
await Task.WhenAll(workers);
Require(counter.Value == 20_000, "原子计数不会丢失更新");

var capped = new CappedCounter(7);
Task<bool>[] attempts = Enumerable.Range(0, 100)
    .Select(_ => Task.Run(capped.TryIncrement)).ToArray();
bool[] accepted = await Task.WhenAll(attempts);
Require(accepted.Count(x => x) == 7 && capped.Value == 7, "CAS 更新不会越过上限");

var store = new MetricsStore();
Task publisher = Task.Run(() =>
{
    for (int i = 1; i <= 5_000; i++) store.Publish(i, i * 2);
});
Task[] readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
{
    for (int i = 0; i < 5_000; i++)
    {
        Metrics snapshot = store.Current;
        Require(snapshot.Total == snapshot.Success + snapshot.Failure, "单次读取的快照内部一致");
    }
})).ToArray();
await Task.WhenAll(readers.Append(publisher));
Require(store.Current.Total == 15_000, "发布者最终快照");

Console.WriteLine("并发原子性示例通过：Interlocked、CAS 与一致快照。");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

public sealed class AtomicCounter
{
    private int _value;
    public int Increment() => Interlocked.Increment(ref _value);
    public int Value => Volatile.Read(ref _value);
}

public sealed class CappedCounter(int limit)
{
    private int _value;
    private readonly int _limit = limit > 0 ? limit : throw new ArgumentOutOfRangeException(nameof(limit));
    public int Value => Volatile.Read(ref _value);

    public bool TryIncrement()
    {
        while (true)
        {
            int observed = Volatile.Read(ref _value);
            if (observed >= _limit) return false;
            if (Interlocked.CompareExchange(ref _value, observed + 1, observed) == observed)
                return true;
        }
    }
}

public sealed record Metrics(int Success, int Failure, int Total);

public sealed class MetricsStore
{
    private Metrics _current = new(0, 0, 0);
    public Metrics Current => Volatile.Read(ref _current);
    public void Publish(int success, int failure) =>
        Volatile.Write(ref _current, new Metrics(success, failure, checked(success + failure)));
}
