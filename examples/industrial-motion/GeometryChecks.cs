namespace IndustrialMotion;

internal static class GeometryChecks
{
    private static int _count;
    private static readonly PlaneAxes Axes = new(2, 0, 4); // Fictional configured mapping, not hardware discovery.
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + label);
        Console.WriteLine($"PASS {++_count}: {label}");
    }
    private static bool Near(double a, double b, double tolerance = 1e-8) => Math.Abs(a - b) <= tolerance;
    private static void Throws<T>(Action action, string label) where T : Exception
    {
        try { action(); } catch (T) { Check(true, label); return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + label);
    }
    private static PlaneArc Center(PlanePoint start, PlanePoint end, PlanePoint center, PlaneArcDirection dir = PlaneArcDirection.CounterClockwise) =>
        InterpolationGeometry.CenterArc(Axes, start, end, center, dir);
    public static void Demo()
    {
        var line = InterpolationGeometry.Line(Axes, new(10, 20), new(60, 80), PlaneCoordinateMode.Relative);
        Console.WriteLine($"Fixture ordered axes [{line.Axes.U},{line.Axes.V}]; relative line end={line.End}, length={line.Length:F3} mm");
        var allowed = new PlaneBox(-.1, -.1, 10.1, 10.1);
        foreach (var dir in Enum.GetValues<PlaneArcDirection>())
        {
            var arc = Center(new(10, 0), new(0, 10), new(0, 0), dir);
            Console.WriteLine($"{dir}: sweep={arc.SweepRadians * 180 / Math.PI:F1} deg; length={arc.Length:F3} mm; box={arc.Bounds}; within fixture box={allowed.Encloses(arc.Bounds)}");
        }
        Console.WriteLine("Numerical mm geometry only. No SDK dispatch, motion timing, collision or physical safety acceptance.");
    }
    public static void Run()
    {
        var start = new PlanePoint(10, 20);
        var relative = InterpolationGeometry.Line(Axes, start, new(60, 80), PlaneCoordinateMode.Relative);
        Check(relative.End == new PlanePoint(70, 100) && Near(relative.Length, 100), "Relative endpoint resolved once from explicit baseline");
        var absolute = InterpolationGeometry.Line(Axes, start, new(70, 100), PlaneCoordinateMode.Absolute);
        Check(absolute == relative, "Same resolved absolute geometry from two input modes");
        Check(relative.Axes.U == 2 && relative.Axes.V == 0, "Axis order preserved, never sorted independently of coordinates");
        Check(relative.Bounds == new PlaneBox(10, 20, 70, 100), "Line extrema are endpoints in a rectangular coordinate box");
        Check(new PlaneBox(10, 20, 70, 100).Encloses(relative.Bounds), "Exact line boundary comparison inclusive");
        Check(!new PlaneBox(11, 20, 70, 100).Encloses(relative.Bounds), "Line outside nominated range rejected by caller");
        var horizontal = InterpolationGeometry.Line(Axes, new(0, 0), new(-10, 0), PlaneCoordinateMode.Absolute);
        Check(Near(horizontal.Length, 10) && horizontal.Bounds == new PlaneBox(-10, 0, 0, 0), "One zero displacement component supported");
        var vertical = InterpolationGeometry.Line(Axes, new(0, 0), new(0, -10), PlaneCoordinateMode.Absolute);
        Check(Near(vertical.Length, 10), "Negative vertical direction supported");
        foreach (var axes in new[] { new PlaneAxes(0, 0, 4), new(0, 4, 4), new(4, 0, 4), new(0, 1, 0) })
            Throws<ArgumentException>(() => InterpolationGeometry.Line(axes, start, new(0, 0), PlaneCoordinateMode.Absolute), "Duplicate/out-of-range/unknown mapping refused: " + axes);
        Throws<ArgumentOutOfRangeException>(() => InterpolationGeometry.Line(Axes, start, new(0, 0), (PlaneCoordinateMode)9), "Undefined coordinate mode refused");
        Throws<ArgumentException>(() => InterpolationGeometry.Line(Axes, start, start, PlaneCoordinateMode.Absolute), "Zero-length line not dispatched as a fake new action");
        Throws<ArgumentException>(() => InterpolationGeometry.Line(Axes, new(0, 0), new(.0001, 0), PlaneCoordinateMode.Absolute), "Near-zero line outside teaching scope");
        foreach (var bad in new[] { new PlanePoint(double.NaN, 0), new(0, double.PositiveInfinity), new(1_000_001, 0) })
            Throws<ArgumentOutOfRangeException>(() => InterpolationGeometry.Line(Axes, start, bad, PlaneCoordinateMode.Absolute), "Invalid input coordinate refused: " + bad);
        Throws<ArgumentOutOfRangeException>(() => InterpolationGeometry.Line(Axes, new(1_000_000, 0), new(1, 0), PlaneCoordinateMode.Relative), "Resolved relative endpoint checked against numerical domain");
        var ccw = Center(new(10, 0), new(0, 10), new(0, 0));
        Check(Near(ccw.Radius, 10) && Near(ccw.EndRadius, 10) && Near(ccw.RadialMismatch, 0), "Center-form equal radii retained");
        Check(Near(ccw.SweepRadians, Math.PI / 2) && Near(ccw.Length, 5 * Math.PI), "CCW quarter-circle signed sweep and reference length");
        var cw = Center(new(10, 0), new(0, 10), new(0, 0), PlaneArcDirection.Clockwise);
        Check(Near(cw.SweepRadians, -3 * Math.PI / 2) && Near(cw.Length, 15 * Math.PI), "Same center/endpoints clockwise follows major arc");
        Check(ccw.Bounds.MinU < 0 && ccw.Bounds.MinU > -.00001 && ccw.Bounds.MinV > -.00001, "Minor-arc box does not unnecessarily include whole circle");
        Check(cw.Bounds.MinU < -10 && cw.Bounds.MinV < -10 && cw.Bounds.MaxU > 10 && cw.Bounds.MaxV > 10, "Major arc includes interior quadrant extrema plus numerical guard");
        var allowed = new PlaneBox(-.1, -.1, 10.1, 10.1);
        Check(allowed.Contains(cw.Start) && allowed.Contains(cw.End), "Both endpoints alone fit nominated box");
        Check(allowed.Encloses(ccw.Bounds) && !allowed.Encloses(cw.Bounds), "Arc interior, not only endpoints, determines numerical range fit");
        Check(!new PlaneBox(0, 0, 10, 10).Encloses(ccw.Bounds), "Guard remains conservative at exact nominal boundary");
        var semi = Center(new(10, 0), new(-10, 0), new(0, 0));
        Check(Near(semi.SweepRadians, Math.PI) && Near(semi.Length, 10 * Math.PI), "Explicit-center semicircle supported");
        Check(semi.Bounds.MinV > -.00001 && semi.Bounds.MaxV > 10, "Semicircle selects correct half-plane extrema");
        var wrap = Center(new(0, -10), new(10, 0), new(0, 0));
        Check(Near(wrap.SweepRadians, Math.PI / 2), "Angle wrapping across zero handled");
        var reversed = Center(ccw.End, ccw.Start, ccw.Center, PlaneArcDirection.Clockwise);
        Check(Near(reversed.Length, ccw.Length) && Near(reversed.Bounds.MinU, ccw.Bounds.MinU), "Reversed endpoints plus direction preserve reference path");
        var shifted = Center(new(110, -50), new(100, -40), new(100, -50));
        Check(Near(shifted.Length, ccw.Length) && Near(shifted.Bounds.MinU - 100, ccw.Bounds.MinU), "Nonzero center translation preserved");
        Throws<ArgumentException>(() => Center(new(0, 0), new(10, 0), new(0, 0)), "Start at center refused");
        Throws<ArgumentException>(() => Center(new(10, 0), new(0, 0), new(0, 0)), "End at center refused");
        Throws<ArgumentException>(() => Center(new(10, 0), new(0, 11), new(0, 0)), "Unequal radii not silently converted to spiral");
        Throws<ArgumentException>(() => Center(new(10, 0), new(10, 0), new(0, 0)), "Coincident endpoint is neither zero move nor implicit whole circle");
        Throws<ArgumentOutOfRangeException>(() => Center(new(10, 0), new(0, 10), new(0, double.NaN)), "Non-finite center refused");
        Throws<ArgumentOutOfRangeException>(() => Center(new(10, 0), new(0, 10), new(0, 0), (PlaneArcDirection)9), "Undefined direction refused");
        var near = Center(new(10, 0), new(0, 10.0000005), new(0, 0));
        Check(near.End.V == 10.0000005 && near.RadialMismatch > 0 && near.RadialMismatch <= InterpolationGeometry.RadialTolerance, "Tiny radial discrepancy disclosed; supplied endpoint not repaired");
        Throws<ArgumentException>(() => Center(new(10, 0), new(0, 10.00001), new(0, 0)), "Larger radial discrepancy refused");
        foreach (var dir in Enum.GetValues<PlaneArcDirection>())
        {
            foreach (double signedRadius in new[] { 10d, -10d })
            {
                var arc = InterpolationGeometry.RadiusArc(Axes, new(10, 0), new(0, 10), signedRadius, dir);
                Check((Math.Abs(arc.SweepRadians) > Math.PI) == (signedRadius < 0) && (arc.SweepRadians > 0) == (dir == PlaneArcDirection.CounterClockwise), "Radius sign selects minor/major, direction selects travel: " + dir + "/" + signedRadius);
                Check(Near(arc.Radius, 10) && Near(arc.EndRadius, 10), "Selected radius-form center satisfies both endpoints");
            }
        }
        var minor = InterpolationGeometry.RadiusArc(Axes, new(10, 0), new(0, 10), 10, PlaneArcDirection.CounterClockwise);
        Check(Near(minor.Center.U, 0) && Near(minor.Center.V, 0), "Known positive-radius CCW fixture selects expected center");
        foreach (double bad in new[] { 0d, double.NaN, double.NegativeInfinity, 1_000_001d })
            Throws<ArgumentOutOfRangeException>(() => InterpolationGeometry.RadiusArc(Axes, new(0, 0), new(1, 0), bad, PlaneArcDirection.Clockwise), "Invalid radius refused: " + bad);
        Throws<ArgumentException>(() => InterpolationGeometry.RadiusArc(Axes, new(0, 0), new(21, 0), 10, PlaneArcDirection.Clockwise), "Chord longer than diameter refused");
        Throws<ArgumentException>(() => InterpolationGeometry.RadiusArc(Axes, new(0, 0), new(20, 0), 10, PlaneArcDirection.Clockwise), "Radius-form semicircle deliberately excluded");
        Throws<ArgumentException>(() => InterpolationGeometry.RadiusArc(Axes, new(0, 0), new(19.9999995, 0), 10, PlaneArcDirection.Clockwise), "Near-semicircle radius branch deliberately excluded");
        Throws<ArgumentException>(() => InterpolationGeometry.RadiusArc(Axes, new(0, 0), new(0, 0), 10, PlaneArcDirection.Clockwise), "Radius form cannot infer whole circle from one point");
        Throws<ArgumentException>(() => new PlaneBox(1, 0, 0, 1).Encloses(ccw.Bounds), "Inverted workspace box refused");
        Throws<ArgumentException>(() => new PlaneBox(0, 0, double.NaN, 1).Contains(new(0, 0)), "Non-finite workspace box refused");
        Check(!allowed.Contains(new(double.NaN, 0)), "Invalid point never treated as inside box");
        bool allInside = true, lengthsAgree = true;
        foreach (var dir in Enum.GetValues<PlaneArcDirection>())
        {
            for (int i = 0; i < 12; i++)
            {
                double first = i * Math.PI / 6, change = (i + 1) * Math.PI / 7;
                var s = new PlanePoint(20 + 10 * Math.Cos(first), -30 + 10 * Math.Sin(first));
                var e = new PlanePoint(20 + 10 * Math.Cos(first + change), -30 + 10 * Math.Sin(first + change));
                var arc = Center(s, e, new(20, -30), dir);
                lengthsAgree &= Near(arc.Length, 10 * Math.Abs(arc.SweepRadians));
                for (int j = 0; j <= 1000; j++)
                {
                    double angle = first + arc.SweepRadians * j / 1000;
                    allInside &= arc.Bounds.Contains(new(20 + 10 * Math.Cos(angle), -30 + 10 * Math.Sin(angle)));
                }
            }
        }
        Check(allInside, "24 direction/wrap fixtures contain 24024 independent trigonometric samples");
        Check(lengthsAgree, "Reference lengths agree across direction/wrap fixtures");
        Console.WriteLine($"{_count} offline geometry checks passed; numerical geometry only, no SDK/card/servo/IO.");
    }
}
