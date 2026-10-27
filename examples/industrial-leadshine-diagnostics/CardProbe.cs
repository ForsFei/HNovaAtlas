namespace IndustrialLeadshineDiagnostics;

// One caller owns this port and serializes ALL calls, including close. No hidden polling/retries.
internal sealed class CardProbe : IDisposable
{
    private readonly IReadOnlyDmc _api;
    private readonly ProbeConfiguration _configuration;
    private readonly TimeProvider _clock;
    private bool _ownsResources, _open, _disposed, _poisoned;
    private ulong _session;
    private CardIdentity? _card;
    public ProbeSnapshot? Latest { get; private set; }
    public string? LastFailure { get; private set; }
    public bool IsOpen => _open;

    public CardProbe(IReadOnlyDmc api, ProbeConfiguration configuration, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate(); _api = api; _configuration = configuration; _clock = clock ?? TimeProvider.System;
    }
    private T Read<T>(Func<ApiValue<T>> call, string operation, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        ApiValue<T> result = call(); // Synchronous native calls would NOT be interrupted by this token.
        cancellation.ThrowIfCancellationRequested();
        if (result.Code != 0) throw new DiagnosticException($"{operation}: SDK return={result.Code}");
        return result.Value;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new DiagnosticException(message);
    }
    public void Open(CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_open || _poisoned) throw new InvalidOperationException("Already open or previous close outcome is uncertain.");
        cancellation.ThrowIfCancellationRequested();
        Latest = null; LastFailure = null;
        try
        {
            _ownsResources = true; // Even failed/partially successful initialization gets one cleanup attempt.
            short count = _api.BoardInit();
            cancellation.ThrowIfCancellationRequested();
            Require(count is >= 1 and <= 8, $"Initialization returned card count/status {count}, not ordinary zero-success.");
            CardIdentity[] cards = Read(_api.Cards, "Card list", cancellation);
            Require(cards != null && cards.Length == count && cards.All(c => c != null && c.Id <= 7) &&
                cards.Select(c => c.Id).Distinct().Count() == count, "Invalid or duplicate card identities/count.");
            _card = cards!.SingleOrDefault(c => c.Id == _configuration.Card);
            Require(_card != null && _card.FirmwareType == _configuration.FirmwareType, "Configured card ID/type was not found.");
            _session = checked(_session + 1); _open = true;
        }
        catch (Exception primary)
        {
            LastFailure = primary.Message;
            try { CloseResources(); }
            catch (Exception cleanup) { throw new AggregateException("Initialization and cleanup both failed.", primary, cleanup); }
            throw;
        }
    }
    public ProbeSnapshot Sample(CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_open) throw new InvalidOperationException("Not open.");
        Latest = null; LastFailure = null; // Invalidate before the FIRST attempted read, including cancellation.
        long start = _clock.GetTimestamp(); DateTimeOffset startedUtc = _clock.GetUtcNow();
        ushort card = _configuration.Card, axis = _configuration.Axis;
        try
        {
            Require(Read(() => _api.ControllerMode(card), "Controller mode", cancellation) == 1, "Controller is not in EtherCAT mode.");
            ushort bus = Read(() => _api.BusError(card, 2), "Bus diagnostic", cancellation);
            Require(bus == 0, $"Bus diagnostic value={bus} (distinct from SDK return).");
            ushort slaves = Read(() => _api.SlaveCount(card, 2), "Slave count", cancellation);
            Require(slaves == _configuration.ExpectedSlaves, "Unexpected slave count; count alone is not slave identity.");
            uint axes = Read(() => _api.AxisCount(card), "Axis count", cancellation);
            Require(axis < axes, "Configured logical axis is outside reported total (including virtual axes).");
            Require(Read(() => _api.AxisType(card, axis), "Axis type", cancellation) == 1, "Logical axis is not EtherCAT type.");
            NodeAddress node = Read(() => _api.AxisNode(card, axis), "Axis mapping", cancellation);
            Require(node == _configuration.ExpectedNode, "Logical axis mapping mismatch.");
            double equivalent = Read(() => _api.Equivalent(card, axis), "Equivalent", cancellation);
            Require(double.IsFinite(equivalent) && equivalent > 0 &&
                Math.Abs(equivalent / _configuration.CountsPerUnit - 1) <= 1e-9, "Invalid or unexpected unit scale.");
            ushort drive = Read(() => _api.DriveState(card, axis), "Drive state", cancellation);
            Require(drive <= 7, "Unknown SDK drive-state enumeration.");
            ushort error = Read(() => _api.AxisError(card, axis), "Axis error", cancellation);
            double position = Read(() => _api.CommandPosition(card, axis), "Command position", cancellation);
            double speed = Read(() => _api.CommandSpeed(card, axis), "Command speed", cancellation);
            Require(double.IsFinite(position) && double.IsFinite(speed), "Nonfinite numeric sample.");
            // These checks detect SOME changes, not atomicity or full node/serial-number identity.
            Require(Read(() => _api.AxisNode(card, axis), "End mapping", cancellation) == node, "Axis mapping changed during sampling.");
            bus = Read(() => _api.BusError(card, 2), "End bus diagnostic", cancellation);
            Require(bus == 0, $"Bus diagnostic changed during sampling: {bus}.");
            long end = _clock.GetTimestamp();
            Require(_clock.GetElapsedTime(start, end) <= _configuration.MaxSampleDuration, "Sample exceeded application time budget.");
            Latest = new(_session, _card!, axis, node, slaves, axes, drive, error, equivalent,
                position, speed, startedUtc, _clock.GetUtcNow(), start, end);
            return Latest;
        }
        catch (Exception error) { LastFailure = error.Message; throw; }
    }
    public bool IsCurrent(ProbeSnapshot snapshot, TimeSpan maxAge)
    {
        if (maxAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxAge));
        return _open && ReferenceEquals(Latest, snapshot) && snapshot.Session == _session &&
            _clock.GetElapsedTime(snapshot.StartedTimestamp) <= maxAge;
    }
    private void CloseResources()
    {
        _open = false; Latest = null; _card = null;
        if (!_ownsResources) return;
        _ownsResources = false; // Never silently repeat a global close whose outcome was uncertain.
        try
        {
            short result = _api.BoardClose();
            Require(result == 0, $"Board close returned {result}; resource outcome uncertain, NOT a stop result.");
        }
        catch { _poisoned = true; throw; }
    }
    public void Close()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CloseResources();
    }
    public void Dispose()
    {
        if (_disposed) return;
        try { CloseResources(); }
        finally { _disposed = true; }
    }
}
