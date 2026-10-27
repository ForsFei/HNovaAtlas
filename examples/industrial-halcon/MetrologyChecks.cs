namespace IndustrialHalcon;

public static class MetrologyChecks
{
    public static void Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            Console.WriteLine($"PASS {++count}: {label}");
        }
        void Reject(Action action, string label)
        {
            try { action(); } catch (ArgumentException) { Check(true, label); return; }
            throw new InvalidOperationException($"Expected rejection: {label}");
        }
        LineInstances Line(double[] p, double[] s) => MetrologyContracts.ReadLines(7, p, s);
        CircleInstances Circle(double[] p, double[] s) => MetrologyContracts.ReadCircles(19, p, s);
        LineInstances noneLine = Line([], []), line = Line([24, 39.5, 104, 39.5], [.9]);
        CircleInstances noneCircle = Circle([], []), circle = Circle([64, 88, 20], [.95]);
        Check(line.ObjectIndex == 7 && circle.ObjectIndex == 19, "Object indices are returned identities, not assumed zero and one");
        Check(line.Instances[0].Begin == new ImagePixel(24, 39.5) && circle.Instances[0].RadiusPixels == 20,
            "Line stride four and circle stride three have different parameter meanings");
        MetrologyReport report = MetrologyContracts.Combine(line, circle);
        Check(report.Outcome == MetrologyOutcome.DimensionsMeasured && report.Dimensions?.DiameterPixels == 40, "One instance of each shape yields diameter, not radius or product OK");
        Check(report.Dimensions?.CenterToInfiniteLinePixels == 48.5, "Center distance is perpendicular to infinite fitted line");
        Check(report.Dimensions?.SignedCircleLineClearancePixels == 28.5, "Circle-to-line signed clearance subtracts radius exactly once");
        Check(MetrologyContracts.Combine(noneLine, noneCircle) is { Outcome: MetrologyOutcome.NoResult, Dimensions: null }, "Empty output remains absent rather than zero dimensions");
        Check(MetrologyContracts.Combine(line, noneCircle) is { Outcome: MetrologyOutcome.MissingObject, Dimensions: null }, "Missing circle blocks combined dimensions");
        Check(MetrologyContracts.Combine(noneLine, circle) is { Outcome: MetrologyOutcome.MissingObject, Dimensions: null }, "Missing line blocks combined dimensions");
        LineInstances twoLines = Line([24, 39.5, 104, 39.5, 24, 41, 104, 41], [.8, 1]);
        CircleInstances twoCircles = Circle([64, 88, 20, 64, 88, 18], [.8, 1]);
        Check(twoLines.Instances[0].Score == .8 && twoLines.Instances[1].Score == 1, "Instance order is preserved, not silently score-sorted");
        Check(MetrologyContracts.Combine(twoLines, circle) is { Outcome: MetrologyOutcome.Ambiguous, Dimensions: null }, "Two line instances do not auto-select first or highest score");
        Check(MetrologyContracts.Combine(line, twoCircles) is { Outcome: MetrologyOutcome.Ambiguous, Dimensions: null }, "Two circle instances do not auto-select first");
        Check(MetrologyContracts.Combine(twoLines, noneCircle).Outcome == MetrologyOutcome.Ambiguous, "Multiplicity is retained even when other shape is missing");
        LineInstances reversed = Line([104, 39.5, 24, 39.5], [1]);
        Check(MetrologyContracts.Combine(reversed, circle).Dimensions == report.Dimensions, "Reversing line endpoints does not change absolute perpendicular distance");
        LineInstances shortLine = Line([0, 39.5, 1, 39.5], [1]);
        Check(MetrologyContracts.Combine(shortLine, circle).Dimensions?.CenterToInfiniteLinePixels == 48.5,
            "Distance does not clamp projection to finite segment endpoints");
        Check(MetrologyContracts.Combine(Line([24, 80, 104, 80], [1]), circle).Dimensions?.SignedCircleLineClearancePixels == -12,
            "Intersecting infinite line yields negative clearance, not absolute value or zero");
        double[] lp = [24, 39.5, 104, 39.5], ls = [.9], cp = [64, 88, 20], cs = [.95];
        LineInstances copiedLine = Line(lp, ls); CircleInstances copiedCircle = Circle(cp, cs);
        lp[1] = cp[2] = ls[0] = cs[0] = 999;
        Check(copiedLine.Instances[0].Begin.Column == 39.5 && copiedLine.Instances[0].Score == .9 &&
            copiedCircle.Instances[0].RadiusPixels == 20 && copiedCircle.Instances[0].Score == .95, "Geometry and scores are detached from native-copy input arrays");
        try { ((IList<FittedLine>)line.Instances)[0] = line.Instances[0]; throw new InvalidOperationException("Read-only lines expected"); }
        catch (NotSupportedException) { Check(true, "Published lines are read-only"); }
        try { ((IList<FittedCircle>)circle.Instances)[0] = circle.Instances[0]; throw new InvalidOperationException("Read-only circles expected"); }
        catch (NotSupportedException) { Check(true, "Published circles are read-only"); }
        Reject(() => Line([24, 40, 104], [.9]), "Partial line tuple refused");
        Reject(() => Circle([64, 88, 20, 0], [.9]), "Circle tuple cannot use line stride");
        Reject(() => Line([], [.9]), "Geometry and score instance counts must agree");
        Reject(() => Circle([64, 88, 20], []), "Missing score cannot manufacture instance quality");
        Reject(() => Line([24, 40, 104, 40], [.79]), "Below configured min_score refused by teaching readback policy");
        Reject(() => Circle([64, 88, 20], [1.01]), "Score is bounded above by one");
        Reject(() => Circle([64, 88, 20], [double.NaN]), "Nonfinite score refused");
        Reject(() => Line([24, double.NaN, 104, 40], [1]), "Nonfinite geometry refused");
        Reject(() => MetrologyContracts.ReadLines(-1, [], []), "Negative object index refused");
        Reject(() => MetrologyContracts.ReadCircles(32, [], []), "Teaching object index cap enforced, not a HALCON universal limit");
        Reject(() => Line([24, 40, 104, 40, 24, 41, 104, 41, 24, 42, 104, 42], [1, 1, 1]), "Teaching maximum two instances enforced");
        Reject(() => Line([24, 40, 24, 40], [1]), "Degenerate line cannot define normal distance");
        Reject(() => Line([-1, 40, 104, 40], [1]), "Fixed uncalibrated image coordinate envelope enforced");
        Reject(() => Circle([64, 88, 0], [1]), "Zero radius refused");
        Reject(() => Circle([64, 64, 49], [1]), "Teaching radius cap enforced");
        Reject(() => Circle([64, 120, 20], [1]), "Partially outside fixture circle refused by this adapter only");
        Reject(() => Line(null!, []), "Missing parameter array refused");
        Reject(() => Circle([], null!), "Missing score array refused");
        Reject(() => MetrologyContracts.Combine(line, MetrologyContracts.ReadCircles(7, [], [])), "Different object types cannot share one object index");
        Reject(() => MetrologyContracts.Combine(null!, circle), "Missing line snapshot refused");
        double[] rows = [24, 64, 104], columns = [39.5, 39.5, 39.5];
        MetrologyDiagnostics diagnostics = MetrologyContracts.ReadDiagnostics(7, 20, rows, columns, 1);
        rows[0] = columns[0] = 999;
        Check(diagnostics.MeasureContourObjects == 20 && diagnostics.DetectedEdges.Count == 3 && diagnostics.ResultContourObjects == 1,
            "Measure-region, candidate-point and result-contour counts are distinct");
        Check(diagnostics.DetectedEdges[0] == new ImagePixel(24, 39.5), "Diagnostic points are copied, not retained array views");
        try { ((IList<ImagePixel>)diagnostics.DetectedEdges)[0] = new(0, 0); throw new InvalidOperationException("Read-only diagnostics expected"); }
        catch (NotSupportedException) { Check(true, "Published diagnostics are read-only"); }
        Check(MetrologyContracts.Combine(line, circle, diagnostics).LineDiagnostics == diagnostics, "Diagnostics retain matching object identity");
        Reject(() => MetrologyContracts.Combine(line, circle, MetrologyContracts.ReadDiagnostics(19, 0, [], [], 0)), "Line diagnostic identity mismatch refused");
        Reject(() => MetrologyContracts.Combine(line, circle, null, diagnostics), "Circle diagnostic identity mismatch refused");
        Reject(() => MetrologyContracts.ReadDiagnostics(7, 20, [24], [], 1), "Diagnostic row and column lengths must agree");
        Reject(() => MetrologyContracts.ReadDiagnostics(7, -1, [], [], 0), "Negative contour count refused");
        Reject(() => MetrologyContracts.ReadDiagnostics(7, 257, [], [], 0), "Measure contour teaching cap enforced");
        Reject(() => MetrologyContracts.ReadDiagnostics(7, 20, new double[257], new double[257], 0), "Candidate-point teaching cap enforced without truncation");
        Reject(() => MetrologyContracts.ReadDiagnostics(7, 20, [], [], 17), "Result contour teaching cap enforced");
        Reject(() => MetrologyContracts.ReadDiagnostics(7, 20, [double.PositiveInfinity], [40], 0), "Nonfinite diagnostic coordinate refused");
        int[] both = MetrologyFixture.Pixels(MetrologyScene.Both);
        Check(both.Length == 16384 && both.All(v => v is 16 or 220), "Original fixture is a bounded 128x128 two-gray-level image");
        Check(both.Count(v => v == 220) == 10007, "Bright half-plane excludes 1257 dark disk pixels");
        Check(both[24 * 128 + 39] == 16 && both[24 * 128 + 40] == 220 && both[64 * 128 + 88] == 16,
            "Vertical transition is ideally column 39.5 and disk center is dark");
        Check(MetrologyFixture.Pixels(MetrologyScene.LineOnly).Count(v => v == 220) == 11264, "Line-only fixture physically removes disk");
        int[] circleOnly = MetrologyFixture.Pixels(MetrologyScene.CircleOnly);
        Check(circleOnly.Count(v => v == 220) == 15127 && circleOnly[24 * 128 + 39] == 220, "Circle-only fixture physically removes vertical step");
        Check(MetrologyFixture.Pixels(MetrologyScene.Blank).All(v => v == 16), "Blank scene contains no fabricated edge responses");
        int[] small = MetrologyFixture.Pixels(MetrologyScene.SmallCircle);
        Check(small.Count(v => v == 220) == 11067 && small[64 * 128 + 88] == 16 && small[64 * 128 + 68] == 220,
            "Radius-eight disk exists but lies outside nominal radius-twenty search band");
        Reject(() => MetrologyFixture.Pixels((MetrologyScene)99), "Unknown fixture scene refused");
        Console.WriteLine($"Metrology managed checks passed: {count}. Output, pixel and geometry contracts only; no edge detection, RANSAC, fitting or HALCON native calls.");
    }
}
