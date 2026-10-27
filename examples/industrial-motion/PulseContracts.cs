namespace IndustrialMotion;

// DMC3000 manual table 8.2 ONLY. These are SDK status bits, not terminal voltages.
[Flags]
internal enum PulseSignal : uint
{
    Alarm = 1u << 0, PositiveLimit = 1u << 1, NegativeLimit = 1u << 2,
    Emergency = 1u << 3, Origin = 1u << 4, PositiveSoftLimit = 1u << 6,
    NegativeSoftLimit = 1u << 7, InPosition = 1u << 8, Index = 1u << 9,
    Ready = 1u << 10, DecelerationInput = 1u << 11
}
internal sealed record PulseIoStatus
{
    public const uint KnownMask = 0xFDF; // Bit 5 and upper bits are not interpreted by this version.
    public uint Raw { get; }
    public uint ConfirmedMask { get; }
    public uint ReservedBits => Raw & ~KnownMask;
    public uint UnconfirmedAssertedBits => Raw & KnownMask & ~ConfirmedMask;
    public PulseIoStatus(uint raw, uint confirmedMask)
    {
        if ((confirmedMask & ~KnownMask) != 0) throw new ArgumentOutOfRangeException(nameof(confirmedMask));
        Raw = raw; ConfirmedMask = confirmedMask;
    }
    public bool? Read(PulseSignal signal)
    {
        uint bit = (uint)signal;
        if (bit == 0 || (bit & (bit - 1)) != 0 || (bit & KnownMask) == 0)
            throw new ArgumentOutOfRangeException(nameof(signal), "Read one documented bit at a time.");
        return (ConfirmedMask & bit) == 0 ? null : (Raw & bit) != 0;
    }
    public bool? BothHardLimitsActive => Read(PulseSignal.PositiveLimit) is bool positive &&
        Read(PulseSignal.NegativeLimit) is bool negative ? positive && negative : null;
}

// Independent scale/sign/origin for each channel. Count is Windows signed 32-bit, NOT C# long.
internal sealed record PulseCountChannel
{
    public decimal CountsPerMillimetre { get; }
    public int Sign { get; }
    public int OriginCount { get; }
    public PulseCountChannel(decimal countsPerMillimetre, int sign = 1, int originCount = 0)
    {
        if (countsPerMillimetre <= 0) throw new ArgumentOutOfRangeException(nameof(countsPerMillimetre));
        if (sign is not (1 or -1)) throw new ArgumentOutOfRangeException(nameof(sign));
        CountsPerMillimetre = countsPerMillimetre; Sign = sign; OriginCount = originCount;
    }
    public int Encode(decimal millimetres)
    {
        decimal delta = decimal.Round(checked(millimetres * CountsPerMillimetre * Sign), 0, MidpointRounding.AwayFromZero);
        return checked((int)checked(OriginCount + delta));
    }
    public decimal Decode(int count) => checked(((decimal)count - OriginCount) * Sign) / CountsPerMillimetre;
}
internal sealed record PulsePositionReading(decimal? CommandMillimetres, decimal? FeedbackMillimetres,
    decimal? DifferenceMillimetres, bool Comparable);
internal sealed class PulsePositionContract(PulseCountChannel command, PulseCountChannel? encoder = null)
{
    private readonly PulseCountChannel _command = command ?? throw new ArgumentNullException(nameof(command));
    public PulsePositionReading Read(int commandCount, int? encoderCount, bool dataValid, bool comparisonContractConfirmed)
    {
        if (encoderCount.HasValue && encoder == null) throw new ArgumentException("Encoder count supplied without its channel contract.");
        if (!dataValid) return new(null, null, null, false);
        decimal commandMm = _command.Decode(commandCount);
        decimal? feedbackMm = encoder != null && encoderCount.HasValue ? encoder.Decode(encoderCount.Value) : null;
        bool comparable = comparisonContractConfirmed && feedbackMm.HasValue;
        return new(commandMm, feedbackMm, comparable ? checked(commandMm - feedbackMm!.Value) : null, comparable);
    }
}
internal enum PulseHomeSpeed { Low = 0, High = 1 }
internal sealed record PulseHomePlan(ushort Direction, PulseHomeSpeed SpeedMode, ushort Mode, ushort ReservedEzCount = 0)
{
    // Checks a small documented subset, NOT a trajectory, hardware capability discovery, or permission to move.
    public void ValidateConfiguration(ushort axis, ushort axisCount, IReadOnlySet<ushort> confirmedModes,
        PulseIoStatus io, bool indexPathConfirmed)
    {
        ArgumentNullException.ThrowIfNull(confirmedModes); ArgumentNullException.ThrowIfNull(io);
        if (axisCount is < 1 or > 12 || axis >= axisCount || Direction > 1 || !Enum.IsDefined(SpeedMode) || ReservedEzCount != 0)
            throw new ArgumentException("Invalid axis/direction/speed selector/reserved field for this teaching baseline.");
        if (Mode > 12 || Mode is >= 6 and <= 9)
            throw new NotSupportedException("Latch modes/unknown modes are outside this teaching checker.");
        if (!confirmedModes.Contains(Mode)) throw new NotSupportedException("Mode not confirmed for this exact axis/firmware.");
        if (Mode == 4 && SpeedMode != PulseHomeSpeed.Low) throw new ArgumentException("Mode 4 is fixed to low-speed homing in this manual.");
        bool needsOrigin = Mode is 0 or 1 or 2 or 3 or 5;
        bool needsIndex = Mode is 3 or 4 or 5;
        if (needsOrigin && !io.Read(PulseSignal.Origin).HasValue) throw new ArgumentException("Origin signal interpretation not confirmed.");
        if (needsIndex && (!indexPathConfirmed || !io.Read(PulseSignal.Index).HasValue))
            throw new ArgumentException("Index input/capability not confirmed; instantaneous EZ level is not a full index test.");
        if (Mode >= 10 && !io.Read(Direction == 0 ? PulseSignal.NegativeLimit : PulseSignal.PositiveLimit).HasValue)
            throw new ArgumentException("Selected homing limit input not confirmed.");
    }
}
internal static class PulseValueContracts
{
    public static bool IsStopped(short runState) => runState switch
    { 0 => false, 1 => true, _ => throw new ArgumentOutOfRangeException(nameof(runState)) };
    public static bool HomeResult(short apiReturnCode, ushort value)
    {
        if (apiReturnCode != 0) throw new InvalidOperationException($"Read home result SDK code={apiReturnCode}");
        return value switch { 0 => false, 1 => true, _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    }
    // For a separately measured RAW input only. Do not apply this again to SDK table-8.2 status bits.
    public static bool RawLevelIsActive(bool high, bool activeHigh) => high == activeHigh;
}
