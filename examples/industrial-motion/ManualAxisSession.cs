namespace IndustrialMotion;

internal readonly record struct JogHandle(Guid Session, ulong Sequence, CommandKey Command);

// Pure simulator owner. All calls are serialized; input authority is NOT a safety-rated permission.
internal sealed class ManualAxisSession : IDisposable
{
    private readonly SimAxis _axis;
    private readonly Guid _identity = Guid.NewGuid();
    private bool _active, _manual, _interlocks, _released, _disposed;
    private ulong _sequence;
    public JogHandle? Held { get; private set; }
    public string LastStopCause { get; private set; } = "None";
    public AxisSnapshot Snapshot => _axis.Read();
    public bool AuthorityAvailable => !_disposed && _active && _manual && _interlocks;
    public bool Armed => AuthorityAvailable && _released && Held == null && Snapshot.DataValid &&
        Snapshot.State == AxisState.Ready && Snapshot.Enabled && Snapshot.Homed;

    public ManualAxisSession(AxisSettings? settings = null, double initialPosition = 1)
        => _axis = new(settings, initialPosition);
    private void Alive() => ObjectDisposedException.ThrowIf(_disposed, this);
    private void RequireAuthority()
    {
        Alive();
        if (!AuthorityAvailable || Held != null) throw new InvalidOperationException("Manual authority/ownership conditions not met.");
    }
    public void SetContext(bool active, bool manual, bool interlocks)
    {
        Alive(); bool wasAvailable = AuthorityAvailable;
        _active = active; _manual = manual; _interlocks = interlocks;
        if (!AuthorityAvailable && (wasAvailable || Held != null)) Stop("AuthorityLost");
    }
    public void ObserveAllReleased()
    {
        Alive();
        if (Held != null) Stop("InputReleased");
        _released = AuthorityAvailable && !Snapshot.Busy;
    }
    public void Open() { Alive(); _axis.Open(); _released = false; }
    public void Enable() { RequireAuthority(); _axis.Enable(); _released = false; }
    public CommandKey Home()
    {
        RequireAuthority(); _released = false; return _axis.Home();
    }
    public void Disable() { Alive(); _axis.Disable(); Held = null; _released = false; }
    public void Close() { Alive(); _axis.Close(); Held = null; _released = false; }
    public void ResetFault()
    {
        RequireAuthority(); _axis.ResetFault(); _released = false; // No enable/home/replay.
    }
    public JogHandle Press(int direction, double velocity)
    {
        Alive();
        if (!Armed) throw new InvalidOperationException("A fresh released input and ready/reference are required.");
        ulong next = checked(_sequence + 1); _released = false;
        CommandKey command = _axis.Jog(direction, velocity);
        _sequence = next; var handle = new JogHandle(_identity, next, command); Held = handle;
        return handle;
    }
    public bool Renew(JogHandle handle)
    {
        Alive();
        if (Held != handle) return false; // Stale events cannot affect a newer hold.
        AxisSnapshot snapshot = Snapshot;
        if (!AuthorityAvailable || !snapshot.DataValid || snapshot.State != AxisState.Jogging || snapshot.Key != handle.Command)
        { Held = null; _released = false; return false; }
        _axis.RenewJog(handle.Command); return true;
    }
    public bool Release(JogHandle handle)
    {
        Alive();
        if (Held != handle) return false;
        Stop("InputReleased"); return true;
    }
    public void Stop(string cause)
    {
        Alive(); Held = null; _released = false; LastStopCause = cause;
        if (Snapshot.Busy) _axis.RequestStop(); // Request only; Tick/observations must still establish stop.
    }
    public void InjectInputs(AxisInputs inputs)
    {
        Alive(); Stop("SimulationInputsChanged"); _axis.SetInputs(inputs);
    }
    public void Tick(double seconds)
    {
        Alive(); _axis.Tick(seconds);
        if (Held is JogHandle handle && (Snapshot.State != AxisState.Jogging || Snapshot.Key != handle.Command))
        { Held = null; _released = false; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        Held = null; _released = false; _axis.Dispose(); _disposed = true;
        // If active, the underlying simulator retains Uncertain. No physical stop guarantee.
    }
}
