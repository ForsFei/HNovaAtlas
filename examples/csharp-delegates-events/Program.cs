Reading[] readings = [new("pump-A", 72.5), new("pump-B", 68.0)];
AlarmRule high = reading => reading.Value >= 70;
Func<Reading, bool> forPumpA = reading => reading.DeviceId == "pump-A";
Require(Count(readings, item => high(item)) == 1, "自定义委托适配");
Require(Count(readings, forPumpA) == 1, "Func 筛选");

int threshold = 70;
Func<int, bool> isAlarm = value => value >= threshold;
Require(isAlarm(75), "初始捕获");
threshold = 80;
Require(!isAlarm(75), "捕获变量的当前值");

List<Func<int>> wrong = [];
List<Func<int>> right = [];
for (int i = 0; i < 3; i++)
{
    wrong.Add(() => i);
    int copy = i;
    right.Add(() => copy);
}
Require(wrong.All(get => get() == 3), "for 闭包共享变量");
Require(right.Select(get => get()).SequenceEqual([0, 1, 2]), "逐轮局部复制");

var monitor = new TemperatureMonitor(70);
var display = new AlarmDisplay(monitor);
monitor.Check("pump-A", 72.5);
Require(display.Count == 1, "事件订阅");
display.Dispose();
monitor.Check("pump-A", 75);
Require(display.Count == 1, "事件退订");

Console.WriteLine("委托与事件示例验证通过：Func、闭包、订阅与退订");

static int Count(IEnumerable<Reading> source, Func<Reading, bool> predicate)
{
    int result = 0;
    foreach (Reading item in source)
        if (predicate(item)) result++;
    return result;
}

static void Require(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException($"验证失败：{label}");
}

public record Reading(string DeviceId, double Value);
public delegate bool AlarmRule(Reading reading);

public sealed class AlarmEventArgs(string deviceId, double value) : EventArgs
{
    public string DeviceId { get; } = deviceId;
    public double Value { get; } = value;
}

public sealed class TemperatureMonitor(double limit)
{
    public event EventHandler<AlarmEventArgs>? AlarmRaised;

    public void Check(string deviceId, double value)
    {
        if (value >= limit)
            AlarmRaised?.Invoke(this, new AlarmEventArgs(deviceId, value));
    }
}

public sealed class AlarmDisplay : IDisposable
{
    private readonly TemperatureMonitor _monitor;
    public int Count { get; private set; }

    public AlarmDisplay(TemperatureMonitor monitor)
    {
        _monitor = monitor;
        _monitor.AlarmRaised += OnAlarm;
    }

    private void OnAlarm(object? sender, AlarmEventArgs args)
    {
        Count++;
        Console.WriteLine($"{args.DeviceId}: {args.Value}");
    }

    public void Dispose() => _monitor.AlarmRaised -= OnAlarm;
}
