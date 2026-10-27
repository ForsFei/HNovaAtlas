namespace IndustrialRobotics;

public static class RobotChecks
{
    private static int count;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine($"PASS {++count}: {label}");
    }
    private static bool Near(Vec3 a, Vec3 b) => Math.Abs(a.X - b.X) < 1e-8
        && Math.Abs(a.Y - b.Y) < 1e-8 && Math.Abs(a.Z - b.Z) < 1e-8;
    private static void Reject(Action action, string label)
    {
        try { action(); } catch (ArgumentException) { Check(true, label); return; }
        throw new InvalidOperationException($"Not rejected: {label}");
    }
    public static void Run()
    {
        count = 0;
        Rotation3 identity = Rotation3.Identity;
        Rotation3 x = Rotation3.Around(RotationAxis.X, Math.PI / 2);
        Rotation3 y = Rotation3.Around(RotationAxis.Y, Math.PI / 2);
        Rotation3 z = Rotation3.Around(RotationAxis.Z, Math.PI / 2);
        Vec3 unitX = new(1, 0, 0), unitY = new(0, 1, 0), unitZ = new(0, 0, 1);
        Check(Near(identity.Apply(new(2, 3, 4)), new(2, 3, 4)), "Identity preserves vector");
        Check(Near(x.Apply(unitY), unitZ), "Right-handed X rotation");
        Check(Near(y.Apply(unitZ), unitX), "Right-handed Y rotation");
        Check(Near(z.Apply(unitX), unitY), "Right-handed Z rotation");
        Check(Near(z.Inverse().Apply(unitY), unitX), "Rotation inverse is transpose");
        Check(Near(z.Compose(x).Apply(unitY), unitZ), "Composition applies inner first");
        Check(!Near(z.Compose(x).Apply(unitY), x.Compose(z).Apply(unitY)), "Rotation order is not interchangeable");
        Check(Near(Rotation3.Around(RotationAxis.Z, -Math.PI / 2).Apply(unitX), -unitY), "Negative rotation");
        Check(Near(Rotation3.Around(RotationAxis.Z, 2 * Math.PI).Apply(unitX), unitX), "Full rotation");
        double[] imported = [1, 0, 0, 0, 1, 0, 0, 0, 1];
        Rotation3 copied = Rotation3.FromRowMajor(imported); imported[0] = 99;
        Check(Near(copied.Apply(unitX), unitX), "Import defensively copied");
        double[] exported = copied.ToRowMajor(); exported[0] = 99;
        Check(Near(copied.Apply(unitX), unitX), "Export cannot mutate rotation");
        Reject(() => Rotation3.FromRowMajor([1, 2]), "Reject wrong matrix size");
        Reject(() => Rotation3.FromRowMajor([1, 0, 0, 0, 1, 0, 0, 0, double.NaN]), "Reject NaN matrix");
        Reject(() => Rotation3.FromRowMajor([2, 0, 0, 0, 1, 0, 0, 0, 1]), "Reject scaling");
        Reject(() => Rotation3.FromRowMajor([1, .1, 0, 0, 1, 0, 0, 0, 1]), "Reject shear");
        Reject(() => Rotation3.FromRowMajor([-1, 0, 0, 0, 1, 0, 0, 0, 1]), "Reject reflection");
        Reject(() => Rotation3.Around((RotationAxis)99, 0), "Reject unknown axis");
        Reject(() => Rotation3.Around(RotationAxis.Z, double.PositiveInfinity), "Reject infinite angle");
        Reject(() => Rotation3.Around(RotationAxis.Z, 90), "Reject unconverted 90 degree input in this teaching range");
        Reject(() => identity.Apply(new(double.NaN, 0, 0)), "Reject nonfinite vector");
        FrameTransform baseFromWork = new("base", "work", z, new(500, 100, 0));
        FramedPoint workPoint = new("work", new(20, 0, 30));
        FramedPoint basePoint = baseFromWork.ApplyPoint(workPoint);
        Check(basePoint.Frame == "base" && Near(basePoint.Millimeters, new(500, 120, 30)), "Rotated fixture point maps to base");
        Check(baseFromWork.Inverse().ApplyPoint(basePoint) is var back && back.Frame == "work"
            && Near(back.Millimeters, workPoint.Millimeters), "Frame inverse roundtrip");
        Check(Near(baseFromWork.RotateVector(unitX), unitY), "Vector rotation does not add translation");
        FrameTransform workFromTcp = new("work", "tcp", identity, new(20, 0, 30));
        FrameTransform baseFromTcp = baseFromWork.Compose(workFromTcp);
        Check(baseFromTcp.Target == "base" && baseFromTcp.Source == "tcp", "Composed frame names");
        Check(Near(baseFromTcp.TranslationMm, basePoint.Millimeters), "Composed TCP origin");
        FrameTransform flangeFromTcp = new("flange", "tcp", identity, new(0, 0, 100));
        FrameTransform baseFromFlange = baseFromTcp.Compose(flangeFromTcp.Inverse());
        Check(Near(baseFromFlange.TranslationMm, new(500, 120, -70)), "Tool offset inverted in correct orientation");
        Check(Near(baseFromFlange.Compose(flangeFromTcp).ApplyPoint(new("tcp", new(5, 6, 7))).Millimeters,
            baseFromTcp.ApplyPoint(new("tcp", new(5, 6, 7))).Millimeters), "Flange/tool reconstruction");
        FrameTransform tiltedTcp = new("base", "tcp", y, new(500, 0, 300));
        Check(Near(tiltedTcp.Compose(flangeFromTcp.Inverse()).TranslationMm, new(400, 0, 300)), "Tilted tool offset is rotated, not base Z subtraction");
        Reject(() => baseFromWork.Compose(flangeFromTcp), "Reject unjoined frame chain");
        Reject(() => baseFromWork.ApplyPoint(new("base", new(0, 0, 0))), "Reject point in wrong frame");
        Reject(() => baseFromWork.ApplyPoint(default), "Reject default unlabeled point");
        Reject(() => new FrameTransform(" ", "work", identity, default), "Reject missing frame");
        Reject(() => new FrameTransform("base", " work", identity, default), "Reject padded frame");
        Reject(() => new FrameTransform("base", "work", identity, new(0, double.NaN, 0)), "Reject invalid translation");
        Reject(() => new FrameTransform("base", "work", identity, new(double.MaxValue, 0, 0))
            .ApplyPoint(new("work", new(double.MaxValue, 0, 0))), "Reject overflowing translation result");
        bool roundtrips = true;
        for (int i = 0; i < 48; i++)
        {
            Rotation3 r = Rotation3.Around(RotationAxis.Z, (i - 24) * .1)
                .Compose(Rotation3.Around(RotationAxis.Y, i * .03))
                .Compose(Rotation3.Around(RotationAxis.X, -i * .04));
            FrameTransform t = new("a", "b", r, new(i * 2, -i, 100));
            FramedPoint p = new("b", new(i, 23, -17));
            roundtrips &= Near(t.Inverse().ApplyPoint(t.ApplyPoint(p)).Millimeters, p.Millimeters);
        }
        Check(roundtrips, "48 deterministic three-axis roundtrips (numerical regression only)");
        RobotBinding binding = new(Guid.Parse("1b4f254b-1170-4a67-ae28-8242b426b515"), 1, 2, 3);
        RobotContextSample good = new(binding, 10, RobotMode.Automatic, true, true, true, true, true);
        bool Observed(RobotContextSample s, double now = 10.1) => RobotContextReview.Assess(binding, s, now).PrerequisitesObserved;
        Check(Observed(good), "Explicit fixture facts can be reviewed; no dispatch");
        Check(!Observed(new(binding, 10)), "Default positive facts are all absent");
        Check(!Observed(good with { Valid = false }), "Invalid snapshot");
        Check(!Observed(good, 10.3), "Stale snapshot");
        Check(!Observed(good with { SampledAt = 11 }), "Future snapshot");
        Check(!Observed(good with { SampledAt = double.NaN }), "Nonfinite sample time");
        Check(!Observed(good with { SampledAt = -1 }), "Negative sample time");
        Check(!Observed(good with { Binding = binding with { ControllerBoot = Guid.NewGuid() } }), "Controller epoch mismatch");
        Check(!Observed(good with { Binding = binding with { Revision = 2 } }), "Calibration/program binding revision mismatch");
        Check(!Observed(good with { Binding = binding with { ToolId = 4 } }), "Tool selection mismatch");
        Check(!Observed(good with { Binding = binding with { WorkFrameId = 4 } }), "Work frame mismatch");
        Check(!Observed(good with { Mode = RobotMode.Teach }), "Teach is not remote automatic");
        Check(!Observed(good with { Mode = (RobotMode)99 }), "Unknown mode enum not accepted");
        Check(!Observed(good with { RemoteSelected = false }), "Remote source absent");
        Check(!Observed(good with { ProgramIdle = false }), "Program idle absent");
        Check(!Observed(good with { NoFaultObserved = false }), "No-fault evidence absent");
        Check(!Observed(good with { ProcessPermit = false }), "Ordinary process permit absent");
        Reject(() => RobotContextReview.Assess(binding with { ControllerBoot = Guid.Empty }, good, 10), "Reject missing expected identity");
        Reject(() => RobotContextReview.Assess(binding with { Revision = 0 }, good, 10), "Reject missing expected revision");
        Reject(() => RobotContextReview.Assess(binding, good, double.NaN), "Reject invalid review clock");
        Reject(() => RobotContextReview.Assess(binding, good, 10, 0), "Reject invalid age budget");
        Check(Observed(good), "Stateless review may pass again; it is not a sticky recovery gate");
        Console.WriteLine($"{count} offline robot value/context checks passed; no IK, SDK, network or motion.");
    }

    public static void Demo()
    {
        FrameTransform baseFromWork = new("base", "work", Rotation3.Around(RotationAxis.Z, Math.PI / 2), new(500, 100, 0));
        FrameTransform workFromTcp = new("work", "tcp", Rotation3.Identity, new(20, 0, 30));
        FrameTransform flangeFromTcp = new("flange", "tcp", Rotation3.Identity, new(0, 0, 100));
        FrameTransform baseFromTcp = baseFromWork.Compose(workFromTcp);
        FrameTransform baseFromFlange = baseFromTcp.Compose(flangeFromTcp.Inverse());
        Console.WriteLine($"TCP origin in base, mm: {baseFromTcp.TranslationMm}");
        Console.WriteLine($"Flange origin in base, mm: {baseFromFlange.TranslationMm}");
        Console.WriteLine("Math only. No joint solution, reachability, collision check or motion command.");
    }
}
