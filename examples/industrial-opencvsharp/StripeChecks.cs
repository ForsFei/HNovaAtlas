using OpenCvSharp;

namespace IndustrialOpenCvSharp;

public static class StripeChecks
{
    public static void Run()
    {
        int count = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); Console.WriteLine($"PASS {++count}: {label}"); }
        void Reject(Action action, string label) { try { action(); } catch (ArgumentException) { Check(true, label); return; } throw new InvalidOperationException(label); }
        static bool Near(double a, double b, double tolerance = 1e-8) => Math.Abs(a - b) <= tolerance;
        StripeRecipe r = StripeDemo.DefaultRecipe();
        using Mat bright = StripeDemo.Fixture(); using Mat before = bright.Clone();
        StripeReport a = StripeDemo.Measure(bright, r);
        Check(a.Outcome == StripeOutcome.WidthMeasured && Near(a.WidthPixels!.Value, 32), "Actual Reduce/Gaussian plus managed pairing measures32px");
        Check(a.MeanProfile.Count == 96 && a.MeanProfile[0] == 20 && a.MeanProfile[30] == 220, "Native projection averages rows into one96-sample row");
        Check(a.Edges.Count == 2 && a.Edges[0].SignedPeak > 0 && a.Edges[1].SignedPeak < 0, "Bright stripe positive/negative along scan");
        Check(Near(a.FirstImagePoint!.Value.X, 39.5) && Near(a.SecondImagePoint!.Value.X, 71.5), "Image coordinates restored at39.5/71.5");
        Check(Near(a.FirstImagePoint.Value.Y, 31.5), "Representative projected centerline y31.5, not actual row-wise edge observations");
        Check(Cv2.Norm(bright, before, NormTypes.INF) == 0, "Borrowed image unchanged");
        Check(a.SmoothedProfile[24] != a.MeanProfile[24], "Actual floating Gaussian changes transition samples");
        StripeReport reverse = StripeDemo.Measure(bright, r with { Reverse = true });
        Check(Near(reverse.WidthPixels!.Value, 32) && reverse.FirstImagePoint == a.SecondImagePoint && reverse.SecondImagePoint == a.FirstImagePoint, "Reverse native profile preserves width and reverses endpoints");
        using Mat dark = StripeDemo.Fixture(dark: true);
        StripeReport d = StripeDemo.Measure(dark, r with { Polarity = StripePolarity.Dark });
        Check(d.Outcome == StripeOutcome.WidthMeasured && Near(d.WidthPixels!.Value, 32) && d.Edges[0].SignedPeak < 0, "Dark groove uses negative/positive pair");
        StripeReport wrong = StripeDemo.Measure(dark, r);
        Check(wrong.Outcome == StripeOutcome.NoPair && wrong.WidthPixels is null, "Wrong contrast recipe publishes no width");
        using Mat horizontal = StripeDemo.Fixture(horizontal: true);
        StripeRecipe yr = new(new(16, 16, 32, 96), ScanAxis.Y);
        StripeReport y = StripeDemo.Measure(horizontal, yr);
        Check(y.Outcome == StripeOutcome.WidthMeasured && Near(y.WidthPixels!.Value, 32), "Y scan reduces columns and smooths one column");
        Check(Near(y.FirstImagePoint!.Value.Y, 39.5) && Near(y.SecondImagePoint!.Value.Y, 71.5) && y.FirstImagePoint.Value.X == 31.5, "Y coordinate mapping restored");
        StripeReport yrReverse = StripeDemo.Measure(horizontal, yr with { Reverse = true });
        Check(yrReverse.FirstImagePoint == y.SecondImagePoint && Near(yrReverse.WidthPixels!.Value, 32), "Reverse Y scan validated");
        using Mat blank = new(128, 128, MatType.CV_8UC1, new Scalar(20));
        StripeReport b = StripeDemo.Measure(blank, r);
        Check(b.Outcome == StripeOutcome.NoEdges && b.WidthPixels is null && b.Pairs.Count == 0, "Blank image absent result, not width0");
        using Mat doubleStripe = StripeDemo.Fixture(doubleStripe: true);
        StripeReport many = StripeDemo.Measure(doubleStripe, r);
        Check(many.Outcome == StripeOutcome.Ambiguous && many.Edges.Count == 4 && many.Pairs.Count == 2 && many.WidthPixels is null, "Two actual stripes ambiguous, no first-pair selection");
        StripeReport single = StripeDemo.Measure(bright, r with { Window = new(16, 16, 48, 32) });
        Check(single.Outcome == StripeOutcome.NoPair && single.Edges.Count == 1 && single.WidthPixels is null, "ROI truncating second side cannot measure width");
        StripeReport boundary = StripeDemo.Measure(bright, r with { Window = new(38, 16, 64, 32) });
        Check(boundary.Outcome == StripeOutcome.BoundaryResponse && boundary.WidthPixels is null, "Response touching profile support guard vetoes width");
        StripeReport weak = StripeDemo.Measure(bright, r with { DifferenceThreshold = 200 });
        Check(weak.Outcome == StripeOutcome.NoEdges, "High difference threshold retains explicit no-edges");
        StripeReport shiftedRoi = StripeDemo.Measure(bright, r with { Window = new(20, 8, 88, 48) });
        Check(Near(shiftedRoi.WidthPixels!.Value, 32) && Near(shiftedRoi.FirstImagePoint!.Value.X, 39.5), "Changing enclosing ROI origin preserves image-axis position");
        using Mat parentRoi = new(bright, new Rect(8, 8, 112, 112)); using Mat clone = parentRoi.Clone();
        StripeRecipe local = new(new(8, 8, 96, 32));
        StripeReport fromView = StripeDemo.Measure(parentRoi, local), fromClone = StripeDemo.Measure(clone, local);
        Check(fromView.SmoothedProfile.SequenceEqual(fromClone.SmoothedProfile), "Noncontinuous input ROI processed like independent clone");
        Check(Near(fromView.FirstImagePoint!.Value.X, 31.5), "Nested input coordinates local to supplied Mat, parent offset not invented");
        Reject(() => StripeDemo.Measure(bright, r with { Window = new(100, 0, 96, 32) }), "Outside ROI rejected, no clipping");
        using Mat color = new(128, 128, MatType.CV_8UC3);
        Reject(() => StripeDemo.Measure(color, r), "Color input rejected");
        using Mat empty = new(); Reject(() => StripeDemo.Measure(empty, r), "Empty input rejected");
        using Mat average = new(32, 64, MatType.CV_8UC1, new Scalar(20));
        Cv2.Rectangle(average, new Rect(20, 0, 20, 16), new Scalar(220), -1);
        StripeReport averaged = StripeDemo.Measure(average, new(new(0, 0, 64, 32)));
        Check(averaged.MeanProfile[24] == 120, "Partial-height stripe averages to120, proving projection can hide incomplete material");
        Check(averaged.Outcome == StripeOutcome.WidthMeasured && Near(averaged.WidthPixels!.Value, 20), "Measured projection is not proof all rows have complete stripe");
        try { ((IList<ScanEdge>)a.Edges)[0] = a.Edges[0]; throw new InvalidOperationException(); }
        catch (NotSupportedException) { Check(true, "Edge snapshot read-only after native Mats released"); }
        Check(a.Pairs[0].WidthPixels == a.WidthPixels && a.Edges[0].FirstDifference < a.Edges[0].LastDifference, "Grouped Gaussian response and pair evidence retained");
        Console.WriteLine($"Native-path stripe checks passed: {count}. Actual Reduce/Gaussian; grouping/pairing managed, no calibrated accuracy or product acceptance.");
    }
}
