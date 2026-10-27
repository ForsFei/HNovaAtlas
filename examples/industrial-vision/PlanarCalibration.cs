namespace IndustrialVision;

// Image points use column/right, row/down; integer coordinates denote pixel centers.
public readonly record struct ImagePoint(double Column, double Row);
public readonly record struct PlanePoint(double Xmm, double Ymm);
public sealed record CalibrationBinding(string SetupId, uint GeometryRevision, string PixelSpace,
    string PlaneFrame, int Width, int Height);
public sealed record CalibrationPair(string Name, ImagePoint Image, PlanePoint Plane);
public sealed record AffineCoefficients(double M11, double M12, double Tx, double M21, double M22, double Ty);
public sealed record CalibrationResidual(string Name, double ErrorXmm, double ErrorYmm, double RadialErrorMm);
public sealed record CalibrationErrorReport(int Count, double RmsMm, double MaxMm,
    double MeanErrorXmm, double MeanErrorYmm, IReadOnlyList<CalibrationResidual> Points);

public static class ImageCoordinates
{
    public static ImagePoint RestoreFromCrop(ImagePoint local, PixelRoi sourceRegion, int fullWidth, int fullHeight)
    {
        _ = Mono8Image.PixelCount(fullWidth, fullHeight);
        sourceRegion.Validate(fullWidth, fullHeight);
        if (!Inside(local, sourceRegion.Width, sourceRegion.Height)) throw new ArgumentException("Point is outside crop pixel-center domain.");
        return new(sourceRegion.X + local.Column, sourceRegion.Y + local.Row);
    }
    internal static bool Inside(ImagePoint p, int width, int height) => double.IsFinite(p.Column) && double.IsFinite(p.Row)
        && p.Column >= 0 && p.Row >= 0 && p.Column <= width - 1 && p.Row <= height - 1;
}

// Three-point exact affine teaching model, NOT lens/hand-eye calibration or a motion permission.
// Mapping is restricted to the fitted image triangle; validation never expands that domain.
public sealed class PlanarAffineCalibration
{
    private const double MinShape = 1e-4, BarycentricTolerance = 1e-10, CoordinateLimit = 1e6;
    private readonly CalibrationPair[] training;
    private readonly double imageScale, worldScale, iu, iv, ju, jv, imageDet, wx, wy, zx, zy, worldDet;
    public CalibrationBinding Binding { get; }
    public AffineCoefficients Coefficients { get; }
    public double ImageTriangleShape { get; }
    public double PlaneTriangleShape { get; }

    private PlanarAffineCalibration(CalibrationBinding binding, CalibrationPair[] points)
    {
        Binding = binding; training = points;
        ImagePoint a = points[0].Image, b = points[1].Image, c = points[2].Image;
        PlanePoint p = points[0].Plane, q = points[1].Plane, r = points[2].Plane;
        (imageScale, iu, iv, ju, jv, imageDet, ImageTriangleShape) = Triangle(b.Column - a.Column, b.Row - a.Row,
            c.Column - a.Column, c.Row - a.Row, minEdge: 1);
        (worldScale, wx, wy, zx, zy, worldDet, PlaneTriangleShape) = Triangle(q.Xmm - p.Xmm, q.Ymm - p.Ymm,
            r.Xmm - p.Xmm, r.Ymm - p.Ymm, minEdge: 1e-6);
        double m11 = worldScale * (wx * jv - zx * iv) / imageDet / imageScale;
        double m12 = worldScale * (iu * zx - ju * wx) / imageDet / imageScale;
        double m21 = worldScale * (wy * jv - zy * iv) / imageDet / imageScale;
        double m22 = worldScale * (iu * zy - ju * wy) / imageDet / imageScale;
        Coefficients = new(m11, m12, p.Xmm - m11 * a.Column - m12 * a.Row,
            m21, m22, p.Ymm - m21 * a.Column - m22 * a.Row);
    }
    private static (double Scale, double U, double V, double S, double T, double Det, double Shape)
        Triangle(double x1, double y1, double x2, double y2, double minEdge)
    {
        double scale = Math.Max(Math.Max(Math.Abs(x1), Math.Abs(y1)), Math.Max(Math.Abs(x2), Math.Abs(y2)));
        if (scale == 0 || !double.IsFinite(scale)) throw new ArgumentException("Duplicate/invalid triangle.");
        double u = x1 / scale, v = y1 / scale, s = x2 / scale, t = y2 / scale;
        double l1 = u * u + v * v, l2 = s * s + t * t, l3 = (u - s) * (u - s) + (v - t) * (v - t);
        double det = u * t - v * s, shape = Math.Abs(det) / Math.Max(l1, Math.Max(l2, l3));
        if (Math.Sqrt(Math.Min(l1, Math.Min(l2, l3))) * scale < minEdge || shape < MinShape)
            throw new ArgumentException("Triangle is too small, collinear or too slender for this teaching policy.");
        return (scale, u, v, s, t, det, shape);
    }
    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 80;
    private static bool FiniteWorld(PlanePoint p) => double.IsFinite(p.Xmm) && double.IsFinite(p.Ymm)
        && Math.Abs(p.Xmm) <= CoordinateLimit && Math.Abs(p.Ymm) <= CoordinateLimit;
    private static void ValidatePair(CalibrationPair pair, CalibrationBinding binding)
    {
        if (pair is null || !Text(pair.Name) || !ImageCoordinates.Inside(pair.Image, binding.Width, binding.Height)
            || !FiniteWorld(pair.Plane)) throw new ArgumentException("Named finite in-image pixel and bounded millimetre point required.");
    }
    public static PlanarAffineCalibration FitThree(CalibrationBinding binding, IReadOnlyList<CalibrationPair> points)
    {
        ArgumentNullException.ThrowIfNull(binding); ArgumentNullException.ThrowIfNull(points);
        if (!Text(binding.SetupId) || !Text(binding.PixelSpace) || !Text(binding.PlaneFrame) || binding.GeometryRevision == 0)
            throw new ArgumentException("Explicit setup, geometry revision, pixel space and target plane required.");
        _ = Mono8Image.PixelCount(binding.Width, binding.Height);
        if (points.Count != 3) throw new ArgumentException("Exactly three correspondences; no multi-point least squares is implemented.");
        CalibrationPair[] snapshot = points.ToArray();
        foreach (CalibrationPair pair in snapshot) ValidatePair(pair, binding);
        if (snapshot.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != 3) throw new ArgumentException("Unique training names required.");
        return new(binding, snapshot);
    }
    private static bool InTriangle(double first, double second) => double.IsFinite(first) && double.IsFinite(second)
        && first >= -BarycentricTolerance && second >= -BarycentricTolerance && first + second <= 1 + BarycentricTolerance;
    public bool TryMap(ImagePoint image, CalibrationBinding current, out PlanePoint plane)
    {
        plane = default;
        if (current != Binding || !ImageCoordinates.Inside(image, Binding.Width, Binding.Height)) return false;
        double u = (image.Column - training[0].Image.Column) / imageScale;
        double v = (image.Row - training[0].Image.Row) / imageScale;
        double first = (u * jv - v * ju) / imageDet, second = (iu * v - iv * u) / imageDet;
        if (!InTriangle(first, second)) return false;
        PlanePoint candidate = new(training[0].Plane.Xmm + worldScale * (first * wx + second * zx),
            training[0].Plane.Ymm + worldScale * (first * wy + second * zy));
        if (!FiniteWorld(candidate)) return false;
        plane = candidate; return true;
    }
    public bool TryUnmap(PlanePoint plane, CalibrationBinding current, out ImagePoint image)
    {
        image = default;
        if (current != Binding || !FiniteWorld(plane)) return false;
        double x = (plane.Xmm - training[0].Plane.Xmm) / worldScale;
        double y = (plane.Ymm - training[0].Plane.Ymm) / worldScale;
        double first = (x * zy - y * zx) / worldDet, second = (wx * y - wy * x) / worldDet;
        if (!InTriangle(first, second)) return false;
        ImagePoint candidate = new(training[0].Image.Column + imageScale * (first * iu + second * ju),
            training[0].Image.Row + imageScale * (first * iv + second * jv));
        // Tiny arithmetic error at an outer vertex is not silently clamped into the image.
        if (!ImageCoordinates.Inside(candidate, Binding.Width, Binding.Height)) return false;
        image = candidate; return true;
    }
    public double MeasureDistance(ImagePoint first, ImagePoint second, CalibrationBinding current)
    {
        if (!TryMap(first, current, out PlanePoint a) || !TryMap(second, current, out PlanePoint b))
            throw new ArgumentException("Both points must match binding and lie in the supported triangle.");
        return Math.Sqrt((a.Xmm - b.Xmm) * (a.Xmm - b.Xmm) + (a.Ymm - b.Ymm) * (a.Ymm - b.Ymm));
    }
    public double PixelPerturbationBound(double radiusPixels)
    {
        if (!double.IsFinite(radiusPixels) || radiusPixels < 0 || radiusPixels > 1000)
            throw new ArgumentOutOfRangeException(nameof(radiusPixels));
        AffineCoefficients m = Coefficients;
        return radiusPixels * Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12 + m.M21 * m.M21 + m.M22 * m.M22);
        // Frobenius-norm upper bound for this fixed linear model only; NOT total uncertainty or confidence.
    }
    public CalibrationErrorReport Evaluate(IReadOnlyList<CalibrationPair> checks, CalibrationBinding current)
    {
        ArgumentNullException.ThrowIfNull(checks);
        if (current != Binding || checks.Count is < 1 or > 64) throw new ArgumentException("Matching binding and 1..64 independent check points required.");
        CalibrationPair[] snapshot = checks.ToArray();
        HashSet<string> names = new(StringComparer.Ordinal); HashSet<ImagePoint> images = [];
        List<CalibrationResidual> residuals = [];
        double squares = 0, maximum = 0, errorX = 0, errorY = 0;
        foreach (CalibrationPair pair in snapshot)
        {
            ValidatePair(pair, Binding);
            if (!names.Add(pair.Name) || !images.Add(pair.Image)
                || training.Any(p => p.Name == pair.Name || p.Image == pair.Image))
                throw new ArgumentException("Do not reuse training identities/positions or duplicate validation points.");
            if (!TryMap(pair.Image, current, out PlanePoint predicted)) throw new ArgumentException("Check point is outside fitted triangle.");
            double dx = predicted.Xmm - pair.Plane.Xmm, dy = predicted.Ymm - pair.Plane.Ymm;
            double squared = dx * dx + dy * dy, radial = Math.Sqrt(squared);
            squares += squared; maximum = Math.Max(maximum, radial); errorX += dx; errorY += dy;
            residuals.Add(new(pair.Name, dx, dy, radial));
        }
        return new(snapshot.Length, Math.Sqrt(squares / snapshot.Length), maximum,
            errorX / snapshot.Length, errorY / snapshot.Length, residuals.AsReadOnly());
    }
}
