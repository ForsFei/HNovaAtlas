using OpenCvSharp;

namespace IndustrialOpenCvSharp;

public static class EdgeChecks
{
    public static void Run()
    {
        int count = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); Console.WriteLine($"PASS {++count}: {label}"); }
        static bool Near(double a, double b, double tolerance = 1e-5) => Math.Abs(a - b) <= tolerance;
        using Mat line = EdgeDemo.LineFixture(); using Mat before = line.Clone();
        EdgeCandidates lines = EdgeDemo.Detect(line, new());
        Check(lines.EdgePixels > 0, "Actual Gaussian/Canny extracts bar boundaries");
        Check(lines.Lines.Any(l => Math.Abs(l.P1.Y - l.P2.Y) <= 1 && Math.Abs(l.P1.X - l.P2.X) >= 60), "HoughLinesP finds a long horizontal candidate, no order assumption");
        Check(Cv2.Norm(line, before, NormTypes.INF) == 0, "Borrowed source unchanged");
        using Mat disk = EdgeDemo.CircleFixture(); EdgeCandidates circles = EdgeDemo.Detect(disk, new());
        Check(circles.Circles.Any(c => Near(c.Center.X, 64, 3) && Near(c.Center.Y, 64, 3) && Near(c.Radius, 24, 3)), "Actual gradient Hough has a disk candidate within teaching tolerance3px");
        Check(circles.EdgePixels > 100 && circles.EdgePixels < 250, "Circle edge pixels are boundary samples, not disk area");
        using Mat blank = new(128, 128, MatType.CV_8UC1, new Scalar(20));
        EdgeCandidates none = EdgeDemo.Detect(blank, new());
        Check(none.EdgePixels == 0 && none.Lines.Count == 0 && none.Circles.Count == 0, "Blank image has explicit empty candidates");
        using Mat tiny = new(8, 8, MatType.CV_8UC1, new Scalar(20));
        Check(EdgeDemo.Detect(tiny, new()).EdgePixels == 0, "Small constant input stays empty");
        using Mat roi = new(line, new Rect(10, 40, 108, 40)); using Mat roiCopy = roi.Clone();
        EdgeCandidates fromRoi = EdgeDemo.Detect(roi, new()), fromCopy = EdgeDemo.Detect(roiCopy, new());
        Check(fromRoi.EdgePixels == fromCopy.EdgePixels && fromRoi.Lines.SequenceEqual(fromCopy.Lines), "ROI parent neighborhood isolated; results local to ROI");
        try { ((IList<LineSegmentPoint>)lines.Lines)[0] = default; throw new InvalidOperationException(); }
        catch (NotSupportedException) { Check(true, "Candidate snapshot read-only"); }
        using Mat wrong = new(8, 8, MatType.CV_8UC3, new Scalar(20));
        try { EdgeDemo.Detect(wrong, new()); throw new InvalidOperationException(); }
        catch (ArgumentException) { Check(true, "Color input rejected before gray algorithm"); }
        LineFit horizontal = EdgeDemo.FitLine([new(0, 2), new(5, 2), new(10, 2)])!;
        Check(Near(horizontal.Y, 2) && Near(horizontal.Vx, 1) && Near(horizontal.RmsPixels, 0), "FitLine L2 horizontal unit direction and orthogonal RMS");
        Check(Near(horizontal.ProjectionStart.X, 0) && Near(horizontal.ProjectionEnd.X, 10), "Projection extent derived from samples, not native segment endpoints");
        LineFit vertical = EdgeDemo.FitLine([new(3, 0), new(3, 4), new(3, 8)])!;
        Check(Near(vertical.X, 3) && Near(Math.Abs(vertical.Vy), 1) && vertical.RmsPixels < 1e-5, "Vertical line uses vector, no infinite slope");
        LineFit slope = EdgeDemo.FitLine([new(0, 1), new(4, 5), new(8, 9)])!;
        Check(Near(slope.Vy / slope.Vx, 1) && slope.RmsPixels < 1e-5, "Slanted line from actual native fit");
        Check(EdgeDemo.FitLine([new(2, 2), new(2, 2)]) is null, "Coincident line samples absent, not fabricated direction");
        LineFit shifted = EdgeDemo.FitLine([new(1000, -498), new(1005, -498), new(1010, -498)])!;
        Check(Near(shifted.X, horizontal.X + 1000) && Near(shifted.Y, horizontal.Y - 500), "Centered float fit restores original coordinates");
        LineFit noisy = EdgeDemo.FitLine([new(0, 0), new(5, 1), new(10, 0), new(15, 0)])!;
        Check(noisy.RmsPixels > 0 && double.IsFinite(noisy.RmsPixels), "Noncollinear line samples retain nonzero residual");
        SamplePoint[] p = EdgeDemo.CircleSamples(); CircleFit fit = EdgeDemo.FitCircle(p)!;
        Check(Near(fit.X, 20) && Near(fit.Y, 30), "Actual SVD algebraic circle center20,30");
        Check(Near(fit.RadiusPixels, 5) && fit.RadialRmsPixels < 1e-10, "Known radius5 and radial residual near0");
        CircleFit moved = EdgeDemo.FitCircle(p.Select(v => new SamplePoint(v.X + 1000, v.Y - 500)).ToArray())!;
        Check(Near(moved.X, 1020) && Near(moved.Y, -470) && Near(moved.RadiusPixels, 5), "Circle translation leaves radius unchanged");
        CircleFit scaled = EdgeDemo.FitCircle(p.Select(v => new SamplePoint(2 * v.X, 2 * v.Y)).ToArray())!;
        Check(Near(scaled.X, 40) && Near(scaled.Y, 60) && Near(scaled.RadiusPixels, 10), "Circle normalization restores scale");
        Check(EdgeDemo.FitCircle(Enumerable.Repeat(new SamplePoint(2, 2), 6).ToArray()) is null, "Collapsed circle samples rejected");
        Check(EdgeDemo.FitCircle(Enumerable.Range(0, 6).Select(i => new SamplePoint(i, 2 * i)).ToArray()) is null, "Collinear circle samples rejected before solve");
        Check(EdgeDemo.FitCircle(Enumerable.Range(0, 8).Select(i => new SamplePoint(Math.Cos(i * .001), Math.Sin(i * .001))).ToArray()) is null,
            "Very short arc fails spread guard, no general arc-coverage proof");
        SamplePoint[] polluted = [.. p, new(35, 30)]; CircleFit biased = EdgeDemo.FitCircle(polluted)!;
        Check(biased.RadialRmsPixels > 1 && Math.Abs(biased.RadiusPixels - 5) > 1, "Algebraic fit is biased by outlier; no automatic robust rejection");
        Cv2.MinEnclosingCircle(p.Select(v => new Point2f((float)v.X, (float)v.Y)), out Point2f center, out float radius);
        Check(Near(center.X, 20, .001) && Near(radius, 5, .001), "Enclosing circle tested separately; coincidence on ideal data not same optimization");
        Check(p.SequenceEqual(EdgeDemo.CircleSamples()), "Fitting does not rewrite supplied point array");
        Console.WriteLine($"Native edge/fitting checks passed: {count}. Actual Canny/Hough/FitLine/Solve; not calibrated measurement or product acceptance.");
    }
}
