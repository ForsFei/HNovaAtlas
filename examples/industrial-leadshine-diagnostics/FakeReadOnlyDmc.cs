namespace IndustrialLeadshineDiagnostics;

// Programmable test double, NOT a Leadshine DLL/firmware/EtherCAT simulation.
internal sealed class ManualClock : TimeProvider
{
    private long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
    public void Advance(TimeSpan span) => _ticks = checked(_ticks + span.Ticks);
}
internal sealed class FakeReadOnlyDmc : IReadOnlyDmc
{
    public static ProbeConfiguration Configuration => new(3, 0x1234, 0, new(1001, 0), 2, 4000, TimeSpan.FromMilliseconds(500));
    public short InitResult = 1, CloseResult;
    public CardIdentity[] Identities = [new(3, 0x1234)]; // Invented fixture type, NOT E3032's type code.
    public ushort Mode = 1, Bus, Slaves = 2, Type = 1, Drive = 1, Alarm;
    public uint Axes = 2;
    public NodeAddress Node = new(1001, 0);
    public double Scale = 4000, Position = 12.5, Speed;
    public string? FailOperation;
    public short ReturnCode = 23;
    public Action<string>? OnCall;
    public readonly List<string> Calls = [];
    private void Call(string name) { Calls.Add(name); OnCall?.Invoke(name); }
    private ApiValue<T> Read<T>(string name, Func<T> value)
    {
        Call(name); return new(FailOperation == name ? ReturnCode : (short)0, value());
    }
    private void Scope(ushort card, ushort? axis = null, ushort? port = null)
    {
        if (card != 3 || (axis.HasValue && axis != 0) || (port.HasValue && port != 2))
            throw new InvalidOperationException("Unexpected fixture card/axis/port.");
    }
    public short BoardInit() { Call("Init"); return InitResult; }
    public short BoardClose() { Call("Close"); return CloseResult; }
    public ApiValue<CardIdentity[]> Cards() => Read("Cards", () => Identities.ToArray());
    public ApiValue<ushort> ControllerMode(ushort card) { Scope(card); return Read("Mode", () => Mode); }
    public ApiValue<ushort> BusError(ushort card, ushort port) { Scope(card, port: port); return Read("Bus", () => Bus); }
    public ApiValue<ushort> SlaveCount(ushort card, ushort port) { Scope(card, port: port); return Read("Slaves", () => Slaves); }
    public ApiValue<uint> AxisCount(ushort card) { Scope(card); return Read("Axes", () => Axes); }
    public ApiValue<ushort> AxisType(ushort card, ushort axis) { Scope(card, axis); return Read("Type", () => Type); }
    public ApiValue<NodeAddress> AxisNode(ushort card, ushort axis) { Scope(card, axis); return Read("Node", () => Node); }
    public ApiValue<ushort> DriveState(ushort card, ushort axis) { Scope(card, axis); return Read("Drive", () => Drive); }
    public ApiValue<ushort> AxisError(ushort card, ushort axis) { Scope(card, axis); return Read("Alarm", () => Alarm); }
    public ApiValue<double> Equivalent(ushort card, ushort axis) { Scope(card, axis); return Read("Scale", () => Scale); }
    public ApiValue<double> CommandPosition(ushort card, ushort axis) { Scope(card, axis); return Read("Position", () => Position); }
    public ApiValue<double> CommandSpeed(ushort card, ushort axis) { Scope(card, axis); return Read("Speed", () => Speed); }
}
