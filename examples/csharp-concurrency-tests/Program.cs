using System.Threading;

var timeout = TimeSpan.FromSeconds(5);
int remaining = 1;
var firstChecked = NewSignal();
var secondChecked = NewSignal();
var release = NewSignal();

async Task<bool> FlawedReserve(TaskCompletionSource<bool> checkedSignal)
{
    int observed = remaining;
    if (observed <= 0) return false;
    checkedSignal.TrySetResult(true);
    await release.Task;
    remaining = observed - 1; // 两个调用都用旧快照写回 0
    return true;
}

Task<bool> first = FlawedReserve(firstChecked);
Task<bool> second = FlawedReserve(secondChecked);
try
{
    await Task.WhenAll(firstChecked.Task, secondChecked.Task).WaitAsync(timeout);
}
finally
{
    release.TrySetResult(true); // 即使测试等待超时，也放开已启动的调用
}

bool[] flawed = await Task.WhenAll(first, second).WaitAsync(timeout);
Require(flawed.All(x => x) && remaining == 0, "稳定复现：一个库存被接受两次，数字未必变负");

var safe = new Inventory(1);
Task<bool>[] attempts = Enumerable.Range(0, 20)
    .Select(_ => Task.Run(safe.TryReserve)).ToArray();
bool[] accepted = await Task.WhenAll(attempts).WaitAsync(timeout);
Require(accepted.Count(x => x) == 1 && safe.Remaining == 0, "检查和扣减在同一锁内");

Console.WriteLine("受控并发测试通过：稳定复现双卖，修复后只有一次预订成功。");

static TaskCompletionSource<bool> NewSignal() =>
    new(TaskCreationOptions.RunContinuationsAsynchronously);

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

public sealed class Inventory(int initial)
{
    private readonly Lock _gate = new();
    private int _remaining = initial >= 0 ? initial : throw new ArgumentOutOfRangeException(nameof(initial));

    public bool TryReserve()
    {
        lock (_gate)
        {
            if (_remaining == 0) return false;
            _remaining--;
            return true;
        }
    }

    public int Remaining
    {
        get { lock (_gate) return _remaining; }
    }
}
