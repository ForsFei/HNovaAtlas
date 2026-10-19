using System.Diagnostics;

using var source = new ActivitySource("HNova.Atlas.Reading", "1.0.0");
var stopped = new List<Snapshot>();
using var listener = new ActivityListener
{
    ShouldListenTo = candidate => candidate.Name == source.Name,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
        ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => stopped.Add(new Snapshot(
        activity.OperationName, activity.TraceId, activity.SpanId,
        activity.ParentSpanId, activity.Status,
        activity.GetTagItem("device.kind")?.ToString()))
};
ActivitySource.AddActivityListener(listener);

Activity? previous = Activity.Current;
ActivityTraceId traceId;
ActivitySpanId parentSpanId;
using (Activity parent = source.StartActivity("read-device", ActivityKind.Internal)
    ?? throw new Exception("listener did not sample parent"))
{
    Check(parent.Recorded, "listener sampled the parent");
    parent.SetTag("device.kind", "temperature");
    traceId = parent.TraceId;
    parentSpanId = parent.SpanId;

    await Task.Yield();
    Check(ReferenceEquals(Activity.Current, parent), "current activity flows across await");

    try { await ParseAsync(source); }
    catch (InvalidDataException)
    {
        parent.SetStatus(ActivityStatusCode.Error, "invalid frame");
    }
    Check(ReferenceEquals(Activity.Current, parent), "child scope restores parent");
}

Check(ReferenceEquals(Activity.Current, previous), "parent scope restores previous context");
Check(stopped.Count == 2 && stopped[0].Name == "parse-frame" &&
    stopped[1].Name == "read-device", "child stops before parent");
Check(stopped.All(item => item.TraceId == traceId), "both operations share trace ID");
Check(stopped[0].ParentSpanId == parentSpanId &&
    stopped[0].SpanId != parentSpanId, "child has its own span ID and parent ID");
Check(stopped.All(item => item.Status == ActivityStatusCode.Error) &&
    stopped[1].DeviceKind == "temperature", "status and safe tag are captured");
Console.WriteLine("Activity 父子关联、跨 await 上下文和错误状态验证通过。");

static async Task ParseAsync(ActivitySource source)
{
    using Activity child = source.StartActivity("parse-frame", ActivityKind.Internal)
        ?? throw new Exception("listener did not sample child");
    Check(ReferenceEquals(Activity.Current, child),
        "child becomes current activity");
    await Task.Yield();
    try { throw new InvalidDataException("bad frame"); }
    catch (InvalidDataException)
    {
        child.SetStatus(ActivityStatusCode.Error, "invalid frame");
        throw;
    }
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

record Snapshot(string Name, ActivityTraceId TraceId, ActivitySpanId SpanId,
    ActivitySpanId ParentSpanId, ActivityStatusCode Status, string? DeviceKind);
