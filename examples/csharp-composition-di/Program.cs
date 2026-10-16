var rule = new HighLimitRule(70);
var production = new AlarmMonitor(rule, new ConsoleAlertSink());
production.Observe(new Reading("pump-A", 72));

var recorder = new RecordingAlertSink();
var tested = new AlarmMonitor(rule, recorder);
tested.Observe(new Reading("pump-B", 65));
tested.Observe(new Reading("pump-B", 75));
Require(recorder.Events.SequenceEqual([new Alert("pump-B", 75)]),
    "只对超限读数通知一次");

var stricter = new AlarmMonitor(new HighLimitRule(80), recorder);
stricter.Observe(new Reading("pump-B", 75));
Require(recorder.Events.Count == 1, "替换规则无需修改 AlarmMonitor");

Console.WriteLine("组合与依赖替换断言通过。");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

public sealed record Reading(string DeviceId, double Value);
public sealed record Alert(string DeviceId, double Value);

public interface IAlarmRule
{
    bool IsAlarm(double value);
}

public sealed class HighLimitRule(double limit) : IAlarmRule
{
    public bool IsAlarm(double value) => value >= limit;
}

public interface IAlertSink
{
    void Send(Alert alert);
}

public sealed class ConsoleAlertSink : IAlertSink
{
    public void Send(Alert alert) =>
        Console.WriteLine($"告警：{alert.DeviceId} = {alert.Value}");
}

public sealed class RecordingAlertSink : IAlertSink
{
    public List<Alert> Events { get; } = [];
    public void Send(Alert alert) => Events.Add(alert);
}

public sealed class AlarmMonitor
{
    private readonly IAlarmRule _rule;
    private readonly IAlertSink _sink;

    public AlarmMonitor(IAlarmRule rule, IAlertSink sink)
    {
        _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public void Observe(Reading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (_rule.IsAlarm(reading.Value))
            _sink.Send(new Alert(reading.DeviceId, reading.Value));
    }
}
