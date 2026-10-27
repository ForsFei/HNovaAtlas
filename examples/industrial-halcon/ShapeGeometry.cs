namespace IndustrialHalcon;

public readonly record struct ImagePixel(double Row, double Column);
public sealed record ShapePose(double RawRowPixels, double RawColumnPixels, double AngleRadians, double Score);
public enum ShapeFollowOutcome { NoMatch, Ambiguous, RoiOutsideImage, RoiPrepared }
public sealed record FollowedRoi(double AreaPixels, double RowPixels, double ColumnPixels, double MeanGray);
public sealed record ShapeFollowReport(ShapeFollowOutcome Outcome, int CandidateCount,
    ShapePose? Pose, IReadOnlyList<ImagePixel> Corners, FollowedRoi? Roi, int OverlayContourObjects);

// Numeric snapshot / geometry checks only. Does not estimate a pose, search a model or rasterize regions.
public sealed class PixelRigidSnapshot
{
    private readonly double[] values;
    private PixelRigidSnapshot(double[] copied) => values = copied;
    public static PixelRigidSnapshot FromValues(double[] input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length != 6 || input.Any(v => !double.IsFinite(v) || Math.Abs(v) > 10_000))
            throw new ArgumentException("Six finite bounded row-major affine coefficients required.");
        double a = input[0], b = input[1], d = input[3], e = input[4];
        const double tolerance = 1e-6;
        if (Math.Abs(a * a + d * d - 1) > tolerance || Math.Abs(b * b + e * e - 1) > tolerance ||
            Math.Abs(a * b + d * e) > tolerance || Math.Abs(a * e - b * d - 1) > tolerance)
            throw new ArgumentException("This teaching adapter supports proper rigid transforms, not scaling/reflection/projective maps.");
        return new(input.ToArray());
    }
    public ImagePixel Map(double row, double column)
    {
        if (!double.IsFinite(row) || !double.IsFinite(column) || Math.Abs(row) > 10_000 || Math.Abs(column) > 10_000)
            throw new ArgumentException("Bounded finite pixel center required.");
        // HALCON AffineTransPixel convention: center -> corner coordinates -> transform -> center.
        double x = row + 0.5, y = column + 0.5;
        return new(values[0] * x + values[1] * y + values[2] - 0.5,
            values[3] * x + values[4] * y + values[5] - 0.5);
    }
    public IReadOnlyList<ImagePixel> Corners(PixelRoi roi)
    {
        roi.Validate(96, 96);
        return Array.AsReadOnly(new[] { Map(roi.Row1, roi.Column1), Map(roi.Row1, roi.Column2),
            Map(roi.Row2, roi.Column2), Map(roi.Row2, roi.Column1) });
    }
    public bool Fits(PixelRoi roi)
    {
        // Deliberately conservative corner-center guard for a convex rectangle. Not a raster/filter-support proof.
        return Corners(roi).All(p => p.Row is >= 0 and <= 95 && p.Column is >= 0 and <= 95);
    }
}

public static class ShapeContracts
{
    public static IReadOnlyList<ShapePose> ReadPoses(double[] rows, double[] columns, double[] angles, double[] scores)
    {
        ArgumentNullException.ThrowIfNull(rows); ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(angles); ArgumentNullException.ThrowIfNull(scores);
        int n = scores.Length;
        if (n > 2 || rows.Length != n || columns.Length != n || angles.Length != n)
            throw new ArgumentException("Four equally sized tuples within requested two-result cap required.");
        ShapePose[] poses = new ShapePose[n];
        for (int i = 0; i < n; i++)
        {
            // Restricted to this 96x96, zero-origin-offset fixture; not HALCON's full legal pose domain.
            if (!double.IsFinite(rows[i]) || rows[i] is < 0 or > 95 ||
                !double.IsFinite(columns[i]) || columns[i] is < 0 or > 95 ||
                !double.IsFinite(angles[i]) || Math.Abs(angles[i]) > 1.7 ||
                !double.IsFinite(scores[i]) || scores[i] is < 0.85 or > 1)
                throw new ArgumentException("Pose violates the fixed fixture output contract.");
            poses[i] = new(rows[i], columns[i], angles[i], scores[i]);
        }
        return Array.AsReadOnly(poses);
    }
    public static FollowedRoi ReadRoi(double area, double row, double column, double mean)
    {
        if (!double.IsFinite(area) || area < 1 || area > 9216 || area != Math.Truncate(area) ||
            !double.IsFinite(row) || row is < 0 or > 95 || !double.IsFinite(column) || column is < 0 or > 95 ||
            !double.IsFinite(mean) || mean is < 0 or > 255)
            throw new ArgumentException("Positive in-image pixel region and finite byte mean required.");
        return new(area, row, column, mean);
    }
}
