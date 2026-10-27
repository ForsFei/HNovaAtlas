using OpenCvSharp;

namespace IndustrialOpenCvSharp;

public sealed record ShapeFeatures(double PolygonArea, double PerimeterPixels, double HullArea,
    double Solidity, double RotatedBoxFill, double Aspect, double Circularity, double CentroidX, double CentroidY,
    double RawBoxAngleDegrees, double LongSidePixels, double ShortSidePixels, double? AxisDegrees,
    IReadOnlyList<Point2f> Corners, IReadOnlyList<Point> Hull, IReadOnlyList<Point> Approximation, bool ApproximationConvex);
public sealed record ShapeCandidate(int ContourIndex, string Reason, ShapeFeatures? Features);

public static class ShapeDemo
{
    // Ordered simple contours from the previous extractor or known teaching polygons, not arbitrary untrusted paths.
    public static ShapeFeatures? Describe(IReadOnlyList<Point> contour, ShapeRules rules)
    {
        ArgumentNullException.ThrowIfNull(contour); ArgumentNullException.ThrowIfNull(rules); rules.Validate();
        if (contour.Count == 0 || contour.Count > 65536 || contour.Any(p => Math.Abs((long)p.X) > 3_000_000 || Math.Abs((long)p.Y) > 3_000_000))
            throw new ArgumentException("Bounded ordered point sequence required.");
        Point[] points = contour.ToArray();
        double area = Cv2.ContourArea(points), perimeter = Cv2.ArcLength(points, true);
        if (points.Length < 3 || area <= 1e-8 || perimeter <= 1e-8) return null;
        Point[] hull = Cv2.ConvexHull(points), approximation = Cv2.ApproxPolyDP(points, rules.EpsilonRatio * perimeter, true);
        double hullArea = Cv2.ContourArea(hull);
        RotatedRect box = Cv2.MinAreaRect(points); Point2f[] corners = box.Points();
        double side1 = box.Size.Width, side2 = box.Size.Height, longSide = Math.Max(side1, side2), shortSide = Math.Min(side1, side2);
        Moments moments = Cv2.Moments(points);
        if (hullArea <= 1e-8 || shortSide <= 1e-8 || Math.Abs(moments.M00) <= 1e-8) return null;
        int edge = Enumerable.Range(0, 4).MaxBy(i => Math.Pow(corners[(i + 1) % 4].X - corners[i].X, 2) + Math.Pow(corners[(i + 1) % 4].Y - corners[i].Y, 2));
        double dx = corners[(edge + 1) % 4].X - corners[edge].X, dy = corners[(edge + 1) % 4].Y - corners[edge].Y;
        double solidity = area / hullArea, fill = area / (longSide * shortSide), aspect = longSide / shortSide;
        double circularity = 4 * Math.PI * area / (perimeter * perimeter), cx = moments.M10 / moments.M00, cy = moments.M01 / moments.M00;
        if (new[] { area, perimeter, hullArea, solidity, fill, aspect, circularity, cx, cy, box.Angle, longSide, shortSide }.Any(v => !double.IsFinite(v)))
            throw new InvalidOperationException("Finite geometry required; not silently clamped.");
        return new(area, perimeter, hullArea, solidity, fill, aspect, circularity, cx, cy, box.Angle, longSide, shortSide,
            ShapeContracts.Direction(dx, dy, longSide, shortSide, rules.SquareRatio), Array.AsReadOnly(corners), Array.AsReadOnly(hull),
            Array.AsReadOnly(approximation), Cv2.IsContourConvex(approximation));
    }
    public static IReadOnlyList<ShapeCandidate> Select(RegionReport report, ShapeRules rules)
    {
        ArgumentNullException.ThrowIfNull(report); rules.Validate();
        ShapeCandidate[] results = report.Contours.Select(c =>
        {
            ShapeFeatures? f = Describe(c.Points, rules);
            string reason = c.Parent != -1 ? "NestedContour" : f is null ? "Degenerate" : rules.RejectChildren && c.Child != -1 ? "HasChildren" :
                f.PolygonArea < rules.MinimumArea ? "Area" : f.Solidity < rules.MinimumSolidity ? "Solidity" : f.RotatedBoxFill < rules.MinimumBoxFill ? "BoxFill" :
                f.Aspect > rules.MaximumAspect ? "Aspect" : f.Approximation.Count != 4 || !f.ApproximationConvex ? "NotConvexQuadrilateral" : "Candidate";
            return new ShapeCandidate(c.Index, reason, f);
        }).ToArray();
        return Array.AsReadOnly(results); // Candidate is a teaching feature match, not product OK or robot pose.
    }
    public static void Run()
    {
        using Mat mask = RegionDemo.Fixture(); RegionReport regions = RegionDemo.Analyze(mask, new());
        foreach (ShapeCandidate item in Select(regions, new()))
            Console.WriteLine($"Contour {item.ContourIndex}: {item.Reason}; area={item.Features?.PolygonArea}, fill={item.Features?.RotatedBoxFill}, aspect={item.Features?.Aspect}, axisDegrees={item.Features?.AxisDegrees}; not product OK.");
    }
}
