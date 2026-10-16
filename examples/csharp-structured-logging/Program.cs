using Microsoft.Extensions.Logging;

var logger = new CaptureLogger();
logger.LogDebug("Skipped reading {ReadingId}", 41);
using (logger.BeginScope(new Dictionary<string, object?>
       {
           ["DeviceKind"] = "temperature"
       }))
{
    logger.LogInformation(new EventId(1001, "ReadingAccepted"),
        "Reading {ReadingId} accepted", 42);
    var error = new InvalidDataException("bad frame");
    logger.LogWarning(new EventId(1002, "ReadingRejected"), error,
        "Reading {ReadingId} failed", 43);
}
logger.LogInformation("Reading {ReadingId} accepted", 44);

Check(logger.Entries.Count == 3, "Debug is filtered");
var accepted = logger.Entries[0];
Check(accepted.Level == LogLevel.Information && accepted.EventId.Id == 1001,
    "level and stable event ID are retained");
Check(accepted.Template == "Reading {ReadingId} accepted" &&
      Equals(accepted.ReadingId, 42), "template and typed field are retained");
Check(Equals(accepted.ScopeDeviceKind, "temperature"), "scope enriches both records");
var rejected = logger.Entries[1];
Check(rejected.Level == LogLevel.Warning && rejected.EventId.Id == 1002 &&
      rejected.Exception is InvalidDataException && Equals(rejected.ReadingId, 43) &&
      Equals(rejected.ScopeDeviceKind, "temperature"),
    "exception object and structured field are retained");
Check(logger.Entries[2].ScopeDeviceKind is null, "scope ends at its boundary");
Console.WriteLine("结构化日志、异常与作用域验证通过。");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

sealed record LogEntry(LogLevel Level, EventId EventId, string? Template,
    object? ReadingId, Exception? Exception, object? ScopeDeviceKind);

sealed class CaptureLogger : ILogger
{
    private IReadOnlyDictionary<string, object?>? _scope;
    public List<LogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        var previous = _scope;
        _scope = state as IReadOnlyDictionary<string, object?>;
        return new RestoreScope(() => _scope = previous);
    }

    public bool IsEnabled(LogLevel logLevel) =>
        logLevel is >= LogLevel.Information and < LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var fields = state as IReadOnlyList<KeyValuePair<string, object?>>;
        object? Field(string name) => fields?.FirstOrDefault(item => item.Key == name).Value;
        Entries.Add(new LogEntry(logLevel, eventId,
            Field("{OriginalFormat}")?.ToString(), Field("ReadingId"), exception,
            _scope?.GetValueOrDefault("DeviceKind")));
    }

    private sealed class RestoreScope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
