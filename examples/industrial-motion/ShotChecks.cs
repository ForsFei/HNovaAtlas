namespace IndustrialMotion;

internal static class ShotChecks
{
    private static int _count;
    private static readonly Guid Arm = Guid.NewGuid(), Camera = Guid.NewGuid();
    private static ShotBaseline Baseline => new(0, Arm, Camera, 7, 100, 42, true);
    private static ShotEvidence New() => new("part-A", Baseline, 10, .05, 0);
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + label);
        Console.WriteLine($"PASS {++_count}: {label}");
    }
    private static void Invalid(ShotBaseline baseline, string label)
    {
        try { _ = new ShotEvidence("part-A", baseline, 10, .05, 0); }
        catch (ArgumentException) { Check(true, label); return; }
        throw new InvalidOperationException("Expected argument failure: " + label);
    }
    private static void Compare(ShotEvidence shot, double time = .1, int count = 8) => shot.ObserveCompare(new(time, Arm, count), time);
    private static void Latch(ShotEvidence shot, double time = .2, double position = 10) => shot.ObserveLatch(new(time, Arm, position, true), time);
    private static void Frame(ShotEvidence shot, double time = .3, ulong id = 101) => shot.ObserveFrame(new(time, Camera, id, 42), time);
    public static void Demo()
    {
        var shot = New(); Compare(shot);
        Console.WriteLine($"Counter increment only: {shot.State}; latch={shot.LatchedPosition}, frame={shot.MatchedFrameId}");
        Latch(shot); Frame(shot);
        Console.WriteLine($"Qualified fixture: part={shot.Workpiece}, state={shot.State}, frame={shot.MatchedFrameId}");
        var ambiguous = New(); Compare(ambiguous); Latch(ambiguous);
        ambiguous.ObserveFrame(new(.3, Camera, 101, null), .3);
        Console.WriteLine($"Frame without verified trigger identity: {ambiguous.State}, {ambiguous.Reason}");
        Console.WriteLine("Scripted metadata only. No SDK/card/pulse/camera/image buffers or physical acceptance.");
    }
    public static void Run()
    {
        var shot = New();
        Check(shot.State == ShotState.Waiting && !shot.ComparisonObserved && shot.LatchedPosition == null && shot.MatchedFrameId == null, "No evidence invented at construction");
        Compare(shot);
        Check(shot.ComparisonObserved && shot.State == ShotState.Waiting && shot.MatchedFrameId == null, "Comparison count not promoted to electrical edge or frame");
        Latch(shot);
        Check(shot.LatchedPosition == 10 && shot.State == ShotState.Waiting, "Latch position alone cannot fabricate an image");
        Frame(shot);
        Check(shot.State == ShotState.EvidenceCorrelated && shot.MatchedFrameId == 101 && shot.Workpiece == "part-A", "Three independently qualified fixture records correlate one shot");
        shot.Invalidate(); shot.CheckDeadline(99); Frame(shot, 99, 999);
        Check(shot.State == ShotState.EvidenceCorrelated && shot.MatchedFrameId == 101, "Correlation is immutable history, not current health or permission");
        shot = New(); Frame(shot, .1); Latch(shot, .2); Compare(shot, .3);
        Check(shot.State == ShotState.EvidenceCorrelated, "Arrival order can differ when identity contracts are explicit");
        shot = New(); Compare(shot, .1, 7);
        Check(!shot.ComparisonObserved && shot.State == ShotState.Waiting, "Unchanged comparison baseline does not mean a new trigger");
        Compare(shot, .2, 8); Compare(shot, .3, 8);
        Check(shot.State == ShotState.Waiting && shot.ComparisonObserved, "New snapshots of same increment do not create extra shots");
        foreach (int bad in new[] { 6, 9, -1 })
        {
            shot = New(); Compare(shot, .1, bad);
            Check(shot.State == ShotState.Unknown, "Counter reset or extra count not unwrapped/remapped: " + bad);
        }
        shot = New(); Compare(shot); Compare(shot, .2, 7);
        Check(shot.State == ShotState.Unknown, "Observed count regression cannot erase prior evidence");
        foreach (var bad in new[] { new CompareEvidence(.1, Guid.NewGuid(), 8), new(.1, Arm, 8, Valid: false), new(.1, Arm, 8, Overflow: true) })
        {
            shot = New(); shot.ObserveCompare(bad, .1);
            Check(shot.State == ShotState.Unknown, "Bad comparator epoch/quality/overflow refused");
        }
        shot = New(); Compare(shot); Compare(shot, .1);
        Check(shot.State == ShotState.Unknown, "Repeated sample timestamp cannot freshen comparator evidence");
        foreach (var bad in new[] { new LatchEvidence(.1, Guid.NewGuid(), 10, true), new(.1, Arm, 10, true, Valid: false),
            new(.1, Arm, 10, true, Overflow: true), new(.1, Arm, 10, false), new(.1, Arm, double.NaN, true), new(.1, Arm, 10.1, true) })
        {
            shot = New(); shot.ObserveLatch(bad, .1);
            Check(shot.State == ShotState.Unknown, "Unqualified or mismatched latch record refused: " + bad);
        }
        shot = New(); Latch(shot, .1, 10.025);
        Check(shot.LatchedPosition == 10.025 && shot.State == ShotState.Waiting, "Confirmed engineering tolerance used without rewriting position");
        Latch(shot, .2, 10.025);
        Check(shot.State == ShotState.Waiting, "Identical latch report at a new observation is not a second capture");
        Latch(shot, .3, 10.03);
        Check(shot.State == ShotState.Unknown, "Conflicting single-shot latch record stops correlation");
        shot = New(); shot.ObserveFrame(new(.1, Camera, 100, 42), .1);
        Check(shot.MatchedFrameId == null && shot.State == ShotState.Waiting, "Pre-arm frame id ignored even if tagged with expected trigger");
        shot.ObserveFrame(new(.2, Camera, 101, 41), .2);
        Check(shot.MatchedFrameId == null && shot.State == ShotState.Waiting, "Known old camera trigger ignored, no FIFO assignment");
        Frame(shot, .3, 102);
        Check(shot.MatchedFrameId == 102, "Frame-id gaps allowed only with explicit expected trigger identity");
        foreach (var bad in new[] { new FrameEvidence(.1, Guid.NewGuid(), 101, 42), new(.1, Camera, 101, null),
            new(.1, Camera, 101, 43), new(.1, Camera, 101, 42, Complete: false) })
        {
            shot = New(); shot.ObserveFrame(bad, .1);
            Check(shot.State == ShotState.Unknown, "Camera epoch/missing identity/unexpected trigger/incomplete frame refused");
        }
        shot = New(); Frame(shot, .1); Frame(shot, .2);
        Check(shot.MatchedFrameId == 101 && shot.State == ShotState.Waiting, "Same frame re-reported at fresh host observation not duplicated");
        Frame(shot, .3, 102);
        Check(shot.State == ShotState.Unknown, "Two distinct frames for one expected trigger are ambiguous");
        shot = New(); shot.ObserveFrame(new(.1, Camera, 101, null), .1); Compare(shot, .2); Latch(shot, .3); Frame(shot, .4);
        Check(shot.State == ShotState.Unknown && shot.MatchedFrameId == null, "Later good data cannot repair unknown attempt or auto-replay");
        shot = New(); shot.ObserveCompare(new(.01, Arm, 8), .5);
        Check(shot.State == ShotState.Unknown, "Old event sample cannot be freshened by new receipt time");
        shot = New(); shot.ObserveLatch(new(.2, Arm, 10, true), .1);
        Check(shot.State == ShotState.Unknown, "Future evidence timestamp refused");
        shot = New(); Compare(shot); shot.CheckDeadline(.05);
        Check(shot.State == ShotState.Unknown, "Backwards monotonic time refused");
        shot = New(); shot.CheckDeadline(double.PositiveInfinity);
        Check(shot.State == ShotState.Unknown, "Non-finite clock refused");
        shot = New(); Compare(shot); Latch(shot); shot.CheckDeadline(1);
        Check(shot.State == ShotState.Unknown && shot.Reason.Contains("NotAStop"), "Missing frame times out, no claim of stopped motion or cancelled exposure");
        shot = New(); shot.ObserveFrame(new(.9, Camera, 100, 41), .9); shot.CheckDeadline(1);
        Check(shot.State == ShotState.Unknown, "Ignored old frame cannot extend total shot deadline");
        shot = New(); Compare(shot, .1); Latch(shot, .2); Frame(shot, 1);
        Check(shot.State == ShotState.Unknown, "Success arriving exactly at deadline cannot complete history");
        shot = New(); shot.Invalidate();
        Check(shot.State == ShotState.Unknown && !shot.ComparisonObserved, "Explicit invalidation sends no stop/reset/output command");
        Invalid(Baseline with { ContractsConfirmed = false }, "Unavailable camera identity or association contracts cannot be assumed");
        Invalid(Baseline with { ArmEpoch = Guid.Empty }, "Empty arm epoch refused");
        Invalid(Baseline with { CameraEpoch = Guid.Empty }, "Empty camera epoch refused");
        Invalid(Baseline with { CompareCount = -1 }, "Negative baseline count refused");
        Invalid(Baseline with { CompareCount = int.MaxValue }, "Count rollover not silently enabled");
        Invalid(Baseline with { FrameId = ulong.MaxValue }, "Frame id rollover not silently enabled");
        Invalid(Baseline with { ExpectedCameraTrigger = 0 }, "Undefined camera trigger ordinal refused");
        Invalid(Baseline with { SampledAt = double.NaN }, "Invalid baseline time refused");
        Invalid(Baseline with { SampledAt = .1 }, "Future baseline time refused");
        foreach (var values in new[] { (double.NaN, .05, 1d, .25), (10d, 0d, 1d, .25), (10d, .05, 0d, .25), (10d, .05, 1d, 0d) })
        {
            bool rejected = false;
            try { _ = new ShotEvidence("part-A", Baseline, values.Item1, values.Item2, 0, values.Item3, values.Item4); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid target/tolerance/deadline/age policy refused");
        }
        bool blankRejected = false;
        try { _ = new ShotEvidence(" ", Baseline, 10, .05, 0); } catch (ArgumentException) { blankRejected = true; }
        Check(blankRejected, "Workpiece label required, not an automatic physical identity");
        Console.WriteLine($"{_count} offline shot-evidence checks passed. Scripted metadata only; no camera/SDK/IO/hardware.");
    }
}
