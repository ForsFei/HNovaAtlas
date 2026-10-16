using System.Diagnostics.Metrics;

using var meter = new Meter("HNova.Atlas.Reading", "1.0.0");
Counter<long> completed = meter.CreateCounter<long>("atlas.readings.completed",
    unit: "{reading}");
Histogram<double> duration = meter.CreateHistogram<double>("atlas.readings.duration",
    unit: "s");
int pending = 2;
meter.CreateObservableGauge<int>("atlas.readings.pending", () => pending,
    unit: "{reading}");

var counts = new List<(long Value, string? Result)>();
var seconds = new List<double>();
var pendingValues = new List<int>();
using var listener = new MeterListener
{
    InstrumentPublished = (instrument, current) =>
    {
        if (instrument.Meter.Name == meter.Name)
            current.EnableMeasurementEvents(instrument);
    }
};
listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
{
    if (instrument.Name == "atlas.readings.completed")
        counts.Add((value, tags.Length > 0 ? tags[0].Value?.ToString() : null));
});
listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
{
    if (instrument.Name == "atlas.readings.duration") seconds.Add(value);
});
listener.SetMeasurementEventCallback<int>((instrument, value, _, _) =>
{
    if (instrument.Name == "atlas.readings.pending") pendingValues.Add(value);
});
listener.Start();

completed.Add(1, new KeyValuePair<string, object?>("result", "ok"));
completed.Add(1, new KeyValuePair<string, object?>("result", "error"));
duration.Record(0.012);
duration.Record(0.034);
listener.RecordObservableInstruments();
pending = 1;
listener.RecordObservableInstruments();

using var otherMeter = new Meter("HNova.Unrelated");
otherMeter.CreateCounter<int>("other.count").Add(1);

Check(counts.SequenceEqual([(1L, "ok"), (1L, "error")]),
    "counter callbacks receive increments and bounded tags");
Check(seconds.SequenceEqual([0.012, 0.034]),
    "histogram callbacks receive samples, not calculated percentiles");
Check(pendingValues.SequenceEqual([2, 1]),
    "observable gauge reads current state only when collected");
Console.WriteLine("Counter、Histogram 与 ObservableGauge 测量验证通过。");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
