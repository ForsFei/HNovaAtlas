namespace IndustrialLeadshineDiagnostics;

// Application port, NOT a native ABI declaration. No commands/configuration writes are exposed.
internal readonly record struct ApiValue<T>(short Code, T Value);
internal sealed record CardIdentity(ushort Id, uint FirmwareType);
internal readonly record struct NodeAddress(ushort Slave, ushort SubAxis);
internal interface IReadOnlyDmc
{
    short BoardInit();
    short BoardClose();
    ApiValue<CardIdentity[]> Cards();
    ApiValue<ushort> ControllerMode(ushort card);
    ApiValue<ushort> BusError(ushort card, ushort port);
    ApiValue<ushort> SlaveCount(ushort card, ushort port);
    ApiValue<uint> AxisCount(ushort card);
    ApiValue<ushort> AxisType(ushort card, ushort axis);
    ApiValue<NodeAddress> AxisNode(ushort card, ushort axis);
    ApiValue<ushort> DriveState(ushort card, ushort axis);
    ApiValue<ushort> AxisError(ushort card, ushort axis);
    ApiValue<double> Equivalent(ushort card, ushort axis);
    ApiValue<double> CommandPosition(ushort card, ushort axis);
    ApiValue<double> CommandSpeed(ushort card, ushort axis);
}

internal sealed record ProbeConfiguration(ushort Card, uint FirmwareType, ushort Axis,
    NodeAddress ExpectedNode, ushort ExpectedSlaves, double CountsPerUnit, TimeSpan MaxSampleDuration)
{
    public void Validate()
    {
        if (Card > 7 || Axis >= 255 || ExpectedSlaves == 0 ||
            !double.IsFinite(CountsPerUnit) || CountsPerUnit <= 0 ||
            MaxSampleDuration <= TimeSpan.Zero || MaxSampleDuration > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(ProbeConfiguration));
    }
}

internal sealed record ProbeSnapshot(ulong Session, CardIdentity Card, ushort Axis, NodeAddress Node,
    ushort SlaveCount, uint TotalAxes, ushort DriveState, ushort AxisError, double CountsPerUnit,
    double CommandPosition, double CommandSpeed, DateTimeOffset StartedUtc, DateTimeOffset EndedUtc,
    long StartedTimestamp, long EndedTimestamp);

internal sealed class DiagnosticException(string message) : Exception(message);
