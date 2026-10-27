using OpenCvSharp;

namespace IndustrialOpenCvSharp;

public static class ShapeChecks
{
    public static void Run()
    {
        int count = 0;
        void Check(bool v, string label) { if (!v) throw new InvalidOperationException(label); Console.WriteLine($"PASS {++count}: {label}"); }
        ShapeRules r = new();
        Point[] rectangle = [new(0, 0), new(10, 0), new(10, 4), new(0, 4)];
        ShapeFeatures f = ShapeDemo.Describe(rectangle, r) ?? throw new InvalidOperationException();
        Check(f.PolygonArea == 40 && f.PerimeterPixels == 28, "Actual polygon area40/perimeter28, not raster area55");
        Check(Math.Abs(f.LongSidePixels - 10) < 1e-5 && Math.Abs(f.ShortSidePixels - 4) < 1e-5 && f.Aspect == 2.5, "MinAreaRect long/short sides independently normalized");
        Check(Math.Abs(f.Solidity - 1) < 1e-6 && Math.Abs(f.RotatedBoxFill - 1) < 1e-6, "Rectangle fills its hull and rotated box");
        Check(f.CentroidX == 5 && f.CentroidY == 2 && f.AxisDegrees == 0, "Contour moments and horizontal undirected axis");
        Check(f.Approximation.Count == 4 && f.ApproximationConvex && f.Corners.Count == 4, "Actual approximation/convexity and returned corners");
        Point[] rotated = [new(10, 10), new(18, 18), new(16, 20), new(8, 12)];
        ShapeFeatures rf = ShapeDemo.Describe(rotated, r)!;
        Check(Math.Abs(rf.PolygonArea - 32) < 1e-6 && Math.Abs(rf.Aspect - 4) < 1e-5 && Math.Abs(rf.AxisDegrees!.Value - 45) < 1e-4,
            "Integer rotated rectangle actual axis45 degrees and ratio4");
        Check(Math.Abs(rf.RotatedBoxFill - 1) < 1e-5 && Math.Abs(rf.LongSidePixels - 8 * Math.Sqrt(2)) < 1e-5, "Minimum box is not axis-aligned boundingRect");
        ShapeFeatures reversed = ShapeDemo.Describe(rectangle.Reverse().ToArray(), r)!;
        Check(Math.Abs(reversed.AxisDegrees!.Value - f.AxisDegrees!.Value) < 1e-5 && reversed.PolygonArea == f.PolygonArea, "Reversing point order retains geometry and undirected axis");
        Point[] square = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
        Check(ShapeDemo.Describe(square, r)!.AxisDegrees is null, "Square has geometry but no unique long-axis output");
        Check(ShapeDemo.Describe([new(1, 1)], r) is null && ShapeDemo.Describe([new(0, 0), new(4, 0)], r) is null,
            "Point/line contours return absent features, not fabricated zero ratios");
        Check(ShapeDemo.Describe([new(0, 0), new(2, 0), new(4, 0)], r) is null, "Collinear three points guarded before division");
        Point[] concave = [new(0, 0), new(8, 0), new(8, 2), new(2, 2), new(2, 8), new(0, 8)];
        ShapeFeatures lf = ShapeDemo.Describe(concave, r)!;
        Check(lf.PolygonArea == 28 && lf.HullArea == 46 && lf.Solidity < .9 && !lf.ApproximationConvex, "Concave L differs from convex hull and is not a rectangle");
        Check(Math.Abs(lf.RotatedBoxFill - 28.0 / 64) < 1e-6, "Low rectangularity quantifies empty box space");
        Check(Math.Abs(f.Circularity - 4 * Math.PI * 40 / (28 * 28)) < 1e-10, "Circularity is computed descriptor, not probability");
        ShapeFeatures translated = ShapeDemo.Describe(rectangle.Select(p => new Point(p.X + 100, p.Y + 200)).ToArray(), r)!;
        Check(translated.CentroidX == 105 && translated.CentroidY == 202 && translated.PolygonArea == 40, "Translation shifts contour centroid, not area");
        try { ((IList<Point2f>)f.Corners)[0] = new(0, 0); throw new InvalidOperationException(); }
        catch (NotSupportedException) { Check(true, "Published corners read-only"); }
        using Mat mask = RegionDemo.Fixture(); RegionReport regions = RegionDemo.Analyze(mask, new());
        IReadOnlyList<ShapeCandidate> chosen = ShapeDemo.Select(regions, r);
        Check(chosen.Count(v => v.Reason == "Candidate") == 1 && chosen.Any(v => v.Reason == "HasChildren") && chosen.Any(v => v.Reason == "NestedContour"),
            "Tree-aware policy rejects hole-bearing root and nested contour explicitly");
        Check(ShapeDemo.Select(regions, r with { RejectChildren = false }).Count(v => v.Reason == "Candidate") == 2,
            "Outer contour geometry alone cannot reveal missing interior material");
        Check(ShapeDemo.Select(regions, r with { MinimumArea = 100 }).All(v => v.Reason != "Candidate"), "No candidate is retained as explicit empty selection");
        using Mat blank = new(4, 4, MatType.CV_8UC1, new Scalar(0));
        Check(ShapeDemo.Select(RegionDemo.Analyze(blank, new()), r).Count == 0, "No contours gives empty feature list");
        Console.WriteLine($"Native shape checks passed: {count}. Actual geometry/selection only; no direction of motion, calibration, object identity or product quality acceptance.");
    }
}
