namespace ReadingAlerts;

public sealed record Reading(string DeviceId, double Value);
public sealed record Alert(string DeviceId, double Value);

public interface IAlertSink
{
    void Send(Alert alert);
}

public sealed class AlarmMonitor
{
    private readonly double _limit;
    private readonly IAlertSink _sink;

    public AlarmMonitor(double limit, IAlertSink sink)
    {
        if (!double.IsFinite(limit)) throw new ArgumentOutOfRangeException(nameof(limit));
        _limit = limit;
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public void Observe(Reading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (string.IsNullOrWhiteSpace(reading.DeviceId))
            throw new ArgumentException("设备 ID 不能为空", nameof(reading));
        if (!double.IsFinite(reading.Value))
            throw new ArgumentException("读数必须是有限数字", nameof(reading));

        if (reading.Value >= _limit)
            _sink.Send(new Alert(reading.DeviceId, reading.Value));
    }
}
