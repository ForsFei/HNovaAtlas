namespace IndustrialMotion;

internal static class PulseChecks
{
    private static int _count;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + label);
        Console.WriteLine($"PASS {++_count}: {label}");
    }
    private static void Throws<T>(Action action, string label) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, label); return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + label);
    }
    public static void Demo()
    {
        var positions = new PulsePositionContract(new(4000), new(10000, -1, 100));
        var reading = positions.Read(50000, -124900, dataValid: true, comparisonContractConfirmed: true);
        Console.WriteLine($"Fixture: command={reading.CommandMillimetres} mm, feedback={reading.FeedbackMillimetres} mm, difference={reading.DifferenceMillimetres} mm");
        Console.WriteLine($"Unconfirmed comparison: difference={positions.Read(50000, -124900, true, false).DifferenceMillimetres?.ToString() ?? "unknown"}");
        uint raw = (uint)(PulseSignal.Ready | PulseSignal.InPosition) | 0x80000000u;
        var io = new PulseIoStatus(raw, (uint)(PulseSignal.Ready | PulseSignal.Origin));
        Console.WriteLine($"Fixture IO: ready={io.Read(PulseSignal.Ready)}, INP={io.Read(PulseSignal.InPosition)?.ToString() ?? "unknown"}, reserved=0x{io.ReservedBits:X8}");
        new PulseHomePlan(0, PulseHomeSpeed.Low, 2).ValidateConfiguration(0, 4, new HashSet<ushort> { 2 }, io, false);
        Console.WriteLine("Teaching home configuration checked, NOT permission to move. No driver/DLL/pulse output or hardware accessed.");
    }
    public static void Run()
    {
        uint union = 0;
        foreach (var signal in Enum.GetValues<PulseSignal>())
        {
            union |= (uint)signal;
            var io = new PulseIoStatus((uint)signal, PulseIoStatus.KnownMask);
            Check(io.Read(signal) == true && Enum.GetValues<PulseSignal>().Where(s => s != signal).All(s => io.Read(s) == false), "Independent documented status bit: " + signal);
        }
        Check(union == PulseIoStatus.KnownMask && (union & (1u << 5)) == 0, "Known mask matches table and leaves bit 5 reserved");
        var unknown = new PulseIoStatus(uint.MaxValue, 0);
        Check(Enum.GetValues<PulseSignal>().All(s => unknown.Read(s) == null), "Unconfirmed signals remain null, not false or true");
        Check(unknown.ReservedBits == ~PulseIoStatus.KnownMask && unknown.UnconfirmedAssertedBits == PulseIoStatus.KnownMask,
            "Reserved and unconfirmed asserted bits retained without signed truncation");
        Check(new PulseIoStatus(6, 6).BothHardLimitsActive == true, "Contradictory hard limits observable");
        Check(new PulseIoStatus(0, 6).BothHardLimitsActive == false, "Known inactive hard limits are distinct from unknown");
        Check(new PulseIoStatus(6, 2).BothHardLimitsActive == null, "One unconfirmed limit prevents fabricated combined state");
        Throws<ArgumentOutOfRangeException>(() => new PulseIoStatus(0, 1u << 5), "Cannot confirm reserved signal mapping");
        Throws<ArgumentOutOfRangeException>(() => unknown.Read(PulseSignal.Alarm | PulseSignal.Ready), "Combined-bit lookup refused");
        Throws<ArgumentOutOfRangeException>(() => unknown.Read((PulseSignal)0), "Zero-bit lookup refused");
        Check(PulseValueContracts.RawLevelIsActive(false, false) && !PulseValueContracts.RawLevelIsActive(true, false) &&
            PulseValueContracts.RawLevelIsActive(true, true) && !PulseValueContracts.RawLevelIsActive(false, true), "Raw polarity conversion kept separate from SDK status decoding");
        Check(!PulseValueContracts.IsStopped(0) && PulseValueContracts.IsStopped(1), "Run-state return is an enum, not zero-success");
        Throws<ArgumentOutOfRangeException>(() => PulseValueContracts.IsStopped(-1), "Unknown run-state rejected");
        Check(!PulseValueContracts.HomeResult(0, 0) && PulseValueContracts.HomeResult(0, 1), "Home result checks API code and output separately");
        Throws<InvalidOperationException>(() => PulseValueContracts.HomeResult(9, 1), "Failed SDK read cannot trust stale home output");
        Throws<ArgumentOutOfRangeException>(() => PulseValueContracts.HomeResult(0, 7), "Unknown home output rejected");
        var command = new PulseCountChannel(4000); var encoder = new PulseCountChannel(10000, -1, 100);
        Check(command.Encode(12.5m) == 50000 && command.Decode(50000) == 12.5m, "Command pulse/mm round trip");
        Check(encoder.Encode(12.5m) == -124900 && encoder.Decode(-124900) == 12.5m, "Independent encoder scale, sign and origin");
        Check(command.Encode(.000125m) == 1 && command.Encode(-.000125m) == -1, "Symmetric half-count rounding");
        var offset = new PulseCountChannel(1, 1, 100);
        Check(offset.Encode(-.5m) == 99 && offset.Encode(.5m) == 101, "Round displacement before adding integer origin");
        var extreme = new PulseCountChannel(1, 1, int.MinValue);
        Check(extreme.Decode(int.MaxValue) == 4294967295m && extreme.Encode(4294967295m) == int.MaxValue, "Wide subtraction and offset avoid intermediate Int32 overflow");
        Throws<OverflowException>(() => command.Encode(1000000m), "Final signed32 overflow is rejected, never wrapped");
        Throws<ArgumentOutOfRangeException>(() => new PulseCountChannel(0), "Zero scale rejected");
        Throws<ArgumentOutOfRangeException>(() => new PulseCountChannel(1, 0), "Direction sign must be plus or minus one");
        var pair = new PulsePositionContract(command, encoder);
        var matched = pair.Read(50000, -124900, true, true);
        Check(matched.Comparable && matched.DifferenceMillimetres == 0, "Compare only after normalizing two independent channels");
        Check(pair.Read(50000, -124800, true, true).DifferenceMillimetres == .01m, "Feedback difference uses engineering units and sign");
        var noContract = pair.Read(50000, -124900, true, false);
        Check(noContract.FeedbackMillimetres == 12.5m && !noContract.Comparable && noContract.DifferenceMillimetres == null,
            "Readable feedback is not automatically a comparable compensation/reference representation");
        var absent = new PulsePositionContract(command).Read(50000, null, true, true);
        Check(absent.FeedbackMillimetres == null && absent.DifferenceMillimetres == null && !absent.Comparable, "Absent encoder not replaced with zero or command position");
        Check(pair.Read(50000, null, true, true).FeedbackMillimetres == null, "Configured encoder with missing sample stays unknown");
        var invalid = pair.Read(50000, -124900, false, true);
        Check(invalid.CommandMillimetres == null && invalid.FeedbackMillimetres == null && !invalid.Comparable, "Invalid batch publishes no decoded positions");
        Throws<ArgumentException>(() => new PulsePositionContract(command).Read(0, 1, true, true), "Encoder count without contract is rejected");
        var supported = new HashSet<ushort> { 0, 1, 2, 3, 4, 5, 10, 11, 12 }; // Explicit TEST fixture, not discovered capabilities.
        var allIo = new PulseIoStatus(0, PulseIoStatus.KnownMask);
        var plan = new PulseHomePlan(0, PulseHomeSpeed.Low, 2);
        plan.ValidateConfiguration(0, 4, supported, allIo, false);
        Check(true, "Configured origin input need not be currently ON to check home configuration");
        Throws<ArgumentException>(() => plan.ValidateConfiguration(4, 4, supported, allIo, false), "Axis range checked against supplied capability fixture");
        Throws<ArgumentException>(() => (plan with { Direction = 2 }).ValidateConfiguration(0, 4, supported, allIo, false), "SDK homing direction is 0/1");
        Throws<ArgumentException>(() => (plan with { ReservedEzCount = 1 }).ValidateConfiguration(0, 4, supported, allIo, false), "Reserved EZ count must remain zero");
        Throws<NotSupportedException>(() => plan.ValidateConfiguration(0, 4, new HashSet<ushort> { 0 }, allIo, false), "No unconfirmed mode support inferred from family name");
        Throws<NotSupportedException>(() => (plan with { Mode = 6 }).ValidateConfiguration(0, 4, supported, allIo, true), "Latch-home modes explicitly outside helper coverage");
        Throws<ArgumentException>(() => plan.ValidateConfiguration(0, 4, supported, new(0, 0), false), "Unknown origin mapping blocks configuration check");
        var indexPlan = plan with { Mode = 4 };
        indexPlan.ValidateConfiguration(0, 4, supported, allIo, true);
        Check(true, "Index-path availability, not instantaneous EZ-high level, is checked");
        Throws<ArgumentException>(() => indexPlan.ValidateConfiguration(0, 4, supported, allIo, false), "Index-required home needs confirmed path");
        Throws<ArgumentException>(() => (indexPlan with { SpeedMode = PulseHomeSpeed.High }).ValidateConfiguration(0, 4, supported, allIo, true), "Mode4 fixed-low contract enforced");
        var limitPlan = plan with { Mode = 10 };
        limitPlan.ValidateConfiguration(0, 4, supported, new(0, (uint)PulseSignal.NegativeLimit), false);
        Check(true, "Negative-limit homing checks the selected input mapping");
        Throws<ArgumentException>(() => (limitPlan with { Direction = 1 }).ValidateConfiguration(0, 4, supported, new(0, (uint)PulseSignal.NegativeLimit), false), "Opposite input mapping is not accepted as positive limit");
        Console.WriteLine($"Completed {_count} pulse-contract checks. Pure values only; no SDK, pulse output or hardware accessed.");
    }
}
