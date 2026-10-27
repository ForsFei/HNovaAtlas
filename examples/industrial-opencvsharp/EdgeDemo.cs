using OpenCvSharp;

namespace IndustrialOpenCvSharp;

public sealed record EdgeCandidates(int EdgePixels, IReadOnlyList<LineSegmentPoint> Lines, IReadOnlyList<CircleSegment> Circles);
public sealed record LineFit(double X, double Y, double Vx, double Vy, double RmsPixels,
    SamplePoint ProjectionStart, SamplePoint ProjectionEnd);
public sealed record CircleFit(double X, double Y, double RadiusPixels, double RadialRmsPixels);

public static class EdgeDemo
{
    public static Mat LineFixture()
    {
        Mat image = new(128, 128, MatType.CV_8UC1, new Scalar(20));
        Cv2.Rectangle(image, new Rect(20, 58, 88, 9), new Scalar(220), -1);
        return image;
    }

    public static Mat CircleFixture()
    {
        Mat image = new(128, 128, MatType.CV_8UC1, new Scalar(20));
        Cv2.Circle(image, new Point(64, 64), 24, new Scalar(220), -1);
        return image;
    }

    // Borrowed gray Mat; clone isolates ROI parent neighborhoods. No candidate-to-fit association is inferred.
    public static EdgeCandidates Detect(Mat gray, EdgeRules rules)
    {
        rules.Validate();
        ArgumentNullException.ThrowIfNull(gray);
        if (gray.Empty() || gray.Dims != 2 || gray.Type() != MatType.CV_8UC1 || gray.Total() > 1048576)
            throw new ArgumentException("Expected nonempty bounded CV_8UC1 gray image.");
        using Mat isolated = gray.Clone(); using Mat smooth = new(); using Mat edges = new();
        Cv2.GaussianBlur(isolated, smooth, new Size(5, 5), 1, 1, BorderTypes.Reflect101);
        Cv2.Canny(smooth, edges, rules.Low, rules.High, rules.Aperture, rules.L2Gradient);
        int edgePixels = Cv2.CountNonZero(edges);
        LineSegmentPoint[] lines = Cv2.HoughLinesP(edges, 1, Math.PI / 180, rules.LineVotes, rules.MinimumLength, rules.MaximumGap);
        // Gradient Hough takes gray, not the Canny map. These radius bounds are fixture-specific.
        CircleSegment[] circles = Cv2.HoughCircles(smooth, HoughModes.Gradient, 1, 32, 100, 20, 20, 28);
        if (lines.Length > 64 || circles.Length > 64) throw new InvalidOperationException("Too many candidates; no silent truncation.");
        if (circles.Any(c => !float.IsFinite(c.Center.X) || !float.IsFinite(c.Center.Y) || !float.IsFinite(c.Radius) || c.Radius <= 0))
            throw new InvalidOperationException("Nonfinite circle candidate.");
        return new(edgePixels, Array.AsReadOnly(lines), Array.AsReadOnly(circles));
    }

    public static LineFit? FitLine(IReadOnlyList<SamplePoint> samples)
    {
        SamplePoint[] p = EdgeContracts.Points(samples, 2);
        double mx = p.Average(v => v.X), my = p.Average(v => v.Y);
        double spread = p.Average(v => (v.X - mx) * (v.X - mx) + (v.Y - my) * (v.Y - my));
        if (spread < 1e-12) return null;
        // Center before float conversion; output origin restored to original image coordinates.
        Point2f[] centered = p.Select(v => new Point2f((float)(v.X - mx), (float)(v.Y - my))).ToArray();
        Line2D fit = Cv2.FitLine(centered, DistanceTypes.L2, 0, .01, .01);
        double norm = Math.Sqrt(fit.Vx * fit.Vx + fit.Vy * fit.Vy);
        if (!double.IsFinite(norm) || norm < 1e-12) throw new InvalidOperationException("Invalid line direction.");
        double vx = fit.Vx / norm, vy = fit.Vy / norm;
        if (vx < -1e-6 || (Math.Abs(vx) <= 1e-6 && vy < 0)) { vx = -vx; vy = -vy; }
        double x = mx + fit.X1, y = my + fit.Y1;
        double[] t = p.Select(v => (v.X - x) * vx + (v.Y - y) * vy).ToArray();
        double rms = Math.Sqrt(p.Average(v => Math.Pow((v.X - x) * vy - (v.Y - y) * vx, 2)));
        if (!double.IsFinite(x + y + rms)) throw new InvalidOperationException("Invalid line result.");
        return new(x, y, vx, vy, rms, new(x + t.Min() * vx, y + t.Min() * vy), new(x + t.Max() * vx, y + t.Max() * vy));
    }

    // Teaching algebraic least squares, NOT geometric least squares, RANSAC or a native FitCircle API.
    public static CircleFit? FitCircle(IReadOnlyList<SamplePoint> samples)
    {
        SamplePoint[] p = EdgeContracts.Points(samples, 6);
        double mx = p.Average(v => v.X), my = p.Average(v => v.Y);
        double xx = p.Average(v => (v.X - mx) * (v.X - mx));
        double yy = p.Average(v => (v.Y - my) * (v.Y - my));
        double xy = p.Average(v => (v.X - mx) * (v.Y - my));
        double trace = xx + yy;
        if (trace < 1e-12 || (xx * yy - xy * xy) / (trace * trace) < 1e-4) return null;
        // The spread guard rejects near-collinearity, not a complete arc-coverage or uncertainty test.
        double scale = Math.Sqrt(trace);
        using Mat a = new(p.Length, 3, MatType.CV_64FC1);
        using Mat b = new(p.Length, 1, MatType.CV_64FC1); using Mat solution = new();
        for (int i = 0; i < p.Length; i++)
        {
            double u = (p[i].X - mx) / scale, v = (p[i].Y - my) / scale;
            a.Set(i, 0, 2 * u); a.Set(i, 1, 2 * v); a.Set(i, 2, 1.0);
            b.Set(i, 0, u * u + v * v);
        }
        if (!Cv2.Solve(a, b, solution, DecompTypes.SVD)) return null;
        double cx = solution.At<double>(0, 0), cy = solution.At<double>(1, 0), c = solution.At<double>(2, 0);
        double radiusSquared = c + cx * cx + cy * cy;
        if (!double.IsFinite(radiusSquared) || radiusSquared <= 0) return null;
        double x = mx + scale * cx, y = my + scale * cy, radius = scale * Math.Sqrt(radiusSquared);
        double rms = Math.Sqrt(p.Average(v => Math.Pow(Math.Sqrt((v.X - x) * (v.X - x) + (v.Y - y) * (v.Y - y)) - radius, 2)));
        if (!double.IsFinite(x + y + radius + rms)) throw new InvalidOperationException("Invalid circle result.");
        return new(x, y, radius, rms);
    }

    public static SamplePoint[] CircleSamples() => [new(25, 30), new(23, 34), new(20, 35), new(17, 34),
        new(15, 30), new(17, 26), new(20, 25), new(23, 26)];

    public static void Run()
    {
        using Mat line = LineFixture(); using Mat circle = CircleFixture();
        foreach ((string name, Mat image) in new[] { ("bar", line), ("disk", circle) })
        {
            EdgeCandidates detected = Detect(image, new());
            Console.WriteLine($"{name}: edge pixels={detected.EdgePixels}, line candidates={detected.Lines.Count}, circle candidates={detected.Circles.Count}");
            foreach (CircleSegment c in detected.Circles) Console.WriteLine($"  Hough candidate center=({c.Center.X:F3},{c.Center.Y:F3}), R={c.Radius:F3}px");
        }
        Console.WriteLine($"Separate associated points, NOT all image edge pixels: {FitCircle(CircleSamples())}");
        Console.WriteLine($"Known line points: {FitLine([new(0, 2), new(5, 2), new(10, 2)])}");
        Console.WriteLine("No calibration, outlier rejection, object identity or product quality acceptance.");
    }
}
