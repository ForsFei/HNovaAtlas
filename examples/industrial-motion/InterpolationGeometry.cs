namespace IndustrialMotion;

internal enum PlaneCoordinateMode { Relative = 0, Absolute = 1 }
internal enum PlaneArcDirection { Clockwise = 0, CounterClockwise = 1 }
internal readonly record struct PlanePoint(double U, double V);
internal readonly record struct PlaneAxes(ushort U, ushort V, ushort ConfiguredAxisCount)
{
    public void Validate()
    {
        if (ConfiguredAxisCount == 0 || U == V || U >= ConfiguredAxisCount || V >= ConfiguredAxisCount)
            throw new ArgumentException("Two distinct configured axis indices required; no discovery performed.");
    }
}
internal readonly record struct PlaneBox(double MinU, double MinV, double MaxU, double MaxV)
{
    public void Validate()
    {
        if (new[] { MinU, MinV, MaxU, MaxV }.Any(v => !double.IsFinite(v)) || MinU > MaxU || MinV > MaxV)
            throw new ArgumentException("Invalid coordinate box.");
    }
    public bool Contains(PlanePoint p)
    {
        Validate();
        return double.IsFinite(p.U) && double.IsFinite(p.V) &&
            p.U >= MinU && p.U <= MaxU && p.V >= MinV && p.V <= MaxV;
    }
    public bool Encloses(PlaneBox other)
    {
        Validate(); other.Validate();
        return other.MinU >= MinU && other.MaxU <= MaxU && other.MinV >= MinV && other.MaxV <= MaxV;
    }
}
internal sealed record PlaneLine(PlaneAxes Axes, PlanePoint Start, PlanePoint End, double Length, PlaneBox Bounds);
// Numerical reference-circle geometry only. No path timing, SDK serialization or physical permission.
internal sealed record PlaneArc(PlaneAxes Axes, PlanePoint Start, PlanePoint End, PlanePoint Center,
    PlaneArcDirection Direction, double Radius, double EndRadius, double SweepRadians, double Length, PlaneBox Bounds)
{
    public double RadialMismatch => Math.Abs(Radius - EndRadius);
}

internal static class InterpolationGeometry
{
    // Teaching-domain assumptions in mm; NOT card limits, precision, clearance or braking margins.
    public const double CoordinateLimit = 1_000_000;
    public const double MinLength = .001;
    public const double RadialTolerance = .000001;
    public const double BoundsGuard = .000001;
    private const double Tau = 2 * Math.PI;

    private static void ValidatePoint(PlanePoint p)
    {
        if (!double.IsFinite(p.U) || !double.IsFinite(p.V) || Math.Abs(p.U) > CoordinateLimit || Math.Abs(p.V) > CoordinateLimit)
            throw new ArgumentOutOfRangeException(nameof(p), "Finite teaching-domain mm coordinates required.");
    }
    private static double Distance(PlanePoint a, PlanePoint b) => Math.Sqrt((a.U - b.U) * (a.U - b.U) + (a.V - b.V) * (a.V - b.V));
    private static double Normalize(double a) { a %= Tau; return a < 0 ? a + Tau : a; }
    private static double DirectedSweep(PlanePoint start, PlanePoint end, PlanePoint center, PlaneArcDirection direction)
    {
        double first = Math.Atan2(start.V - center.V, start.U - center.U);
        double last = Math.Atan2(end.V - center.V, end.U - center.U);
        return direction == PlaneArcDirection.CounterClockwise ? Normalize(last - first) : -Normalize(first - last);
    }
    private static PlaneBox Box(IEnumerable<PlanePoint> points, double guard = 0)
    {
        var values = points.ToArray();
        return new(values.Min(p => p.U) - guard, values.Min(p => p.V) - guard,
            values.Max(p => p.U) + guard, values.Max(p => p.V) + guard);
    }
    public static PlaneLine Line(PlaneAxes axes, PlanePoint start, PlanePoint target, PlaneCoordinateMode mode)
    {
        axes.Validate(); ValidatePoint(start); ValidatePoint(target);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        PlanePoint end = mode == PlaneCoordinateMode.Absolute ? target : new(start.U + target.U, start.V + target.V);
        ValidatePoint(end);
        double length = Distance(start, end);
        if (length < MinLength) throw new ArgumentException("Zero/near-zero line outside this helper's scope.");
        return new(axes, start, end, length, Box([start, end]));
    }
    public static PlaneArc CenterArc(PlaneAxes axes, PlanePoint start, PlanePoint end, PlanePoint center,
        PlaneArcDirection direction)
    {
        axes.Validate(); ValidatePoint(start); ValidatePoint(end); ValidatePoint(center);
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        double r0 = Distance(start, center), r1 = Distance(end, center);
        if (r0 < MinLength || r1 < MinLength || Distance(start, end) < MinLength)
            throw new ArgumentException("Zero radius, whole circle and near-coincident endpoints are unsupported.");
        if (Math.Abs(r0 - r1) > RadialTolerance)
            throw new ArgumentException("Unequal radii: no spiral or automatic repair supported.");
        double sweep = DirectedSweep(start, end, center, direction);
        if (Math.Abs(sweep) <= 0 || Math.Abs(sweep) >= Tau) throw new ArgumentException("Degenerate sweep.");
        double first = Math.Atan2(start.V - center.V, start.U - center.U);
        double outerRadius = Math.Max(r0, r1);
        var extrema = new List<PlanePoint> { start, end };
        for (int i = 0; i < 4; i++)
        {
            double angle = i * Math.PI / 2;
            double travel = direction == PlaneArcDirection.CounterClockwise ? Normalize(angle - first) : Normalize(first - angle);
            if (travel <= Math.Abs(sweep) + 1e-12)
                extrema.Add(new(center.U + outerRadius * Math.Cos(angle), center.V + outerRadius * Math.Sin(angle)));
        }
        // Guard only accounts for this numerical model; NOT machine following/stop/clearance error.
        return new(axes, start, end, center, direction, r0, r1, sweep, r0 * Math.Abs(sweep), Box(extrema, BoundsGuard));
    }
    public static PlaneArc RadiusArc(PlaneAxes axes, PlanePoint start, PlanePoint end,
        double signedRadius, PlaneArcDirection direction)
    {
        axes.Validate(); ValidatePoint(start); ValidatePoint(end);
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (!double.IsFinite(signedRadius) || Math.Abs(signedRadius) < MinLength || Math.Abs(signedRadius) > CoordinateLimit)
            throw new ArgumentOutOfRangeException(nameof(signedRadius));
        double r = Math.Abs(signedRadius), chord = Distance(start, end);
        if (chord < MinLength || chord > 2 * r) throw new ArgumentException("Coincident endpoints or chord longer than diameter.");
        if (2 * r - chord <= RadialTolerance)
            throw new ArgumentException("Near-semicircle radius form excluded; use explicit-center geometry.");
        double half = chord / 2;
        double height = Math.Sqrt((r - half) * (r + half));
        var mid = new PlanePoint((start.U + end.U) / 2, (start.V + end.V) / 2);
        var normal = new PlanePoint(-(end.V - start.V) / chord, (end.U - start.U) / chord);
        var a = new PlanePoint(mid.U + height * normal.U, mid.V + height * normal.V);
        var b = new PlanePoint(mid.U - height * normal.U, mid.V - height * normal.V);
        bool major = signedRadius < 0;
        var center = (Math.Abs(DirectedSweep(start, end, a, direction)) > Math.PI) == major ? a : b;
        PlaneArc arc = CenterArc(axes, start, end, center, direction);
        if ((Math.Abs(arc.SweepRadians) > Math.PI) != major) throw new ArgumentException("Ambiguous radius branch.");
        return arc;
    }
}
