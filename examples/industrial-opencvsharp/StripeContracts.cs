namespace IndustrialOpenCvSharp;

public static class StripeContracts
{
    public static void Run()
    {
        int count = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); Console.WriteLine($"PASS {++count}: {label}"); }
        void Reject(Action action, string label) { try { action(); } catch (ArgumentException) { Check(true, label); return; } throw new InvalidOperationException(label); }
        StripeRecipe r = new(new(10, 20, 32, 8));
        r.ValidateImage(64, 64); Check(true, "Default managed recipe valid");
        Reject(() => (r with { Axis = (ScanAxis)99 }).Validate(), "Unknown scan axis");
        Reject(() => (r with { Polarity = (StripePolarity)99 }).Validate(), "Unknown polarity");
        Reject(() => (r with { Window = new(-1, 0, 32, 8) }).Validate(), "Negative ROI origin");
        Reject(() => (r with { Window = new(0, 0, 15, 8) }).Validate(), "Minimum scan length");
        Reject(() => (r with { Window = new(0, 0, 4097, 8) }).Validate(), "Maximum scan length");
        Reject(() => (r with { Window = new(0, 0, 32, 257) }).Validate(), "Averaging width bounded");
        Reject(() => (r with { DifferenceThreshold = 0 }).Validate(), "Positive derivative threshold");
        Reject(() => (r with { DifferenceThreshold = double.NaN }).Validate(), "Finite derivative threshold");
        Reject(() => (r with { DifferenceThreshold = 256 }).Validate(), "Derivative range bounded");
        Reject(() => (r with { Sigma = .1 }).Validate(), "Sigma lower bound");
        Reject(() => (r with { Sigma = double.PositiveInfinity }).Validate(), "Finite sigma");
        Reject(() => r.ValidateImage(40, 64), "No ROI clipping");
        Reject(() => r.ValidateImage(4096, 4096), "Input image pixel capacity");
        Reject(() => (r with { Window = new(int.MaxValue, 0, 32, 8) }).ValidateImage(64, 64), "Huge origin rejected without overflow");
        double[] p = Enumerable.Range(0, 32).Select(i => i is >= 8 and < 20 ? 220.0 : 20.0).ToArray();
        StripeReport a = StripeAnalysis.Analyze(p, p, r);
        Check(a.Outcome == StripeOutcome.WidthMeasured && a.WidthPixels == 12 && a.Edges.Count == 2, "Synthetic unsmoothed profile yields width12; no native processing");
        Check(a.Edges[0].Position == 7.5 && a.Edges[1].Position == 19.5, "Difference located between pixel centers");
        Check(a.FirstImagePoint == new SamplePoint(17.5, 23.5) && a.SecondImagePoint == new SamplePoint(29.5, 23.5), "ROI origin and representative centerline restored");
        StripeReport reverse = StripeAnalysis.Analyze(p, p, r with { Reverse = true });
        Check(reverse.WidthPixels == 12 && reverse.FirstImagePoint == a.SecondImagePoint && reverse.SecondImagePoint == a.FirstImagePoint, "Reverse changes ordered endpoints, not positive width");
        Check(StripeAnalysis.Analyze(p, p, r with { Polarity = StripePolarity.Dark }).Outcome == StripeOutcome.NoPair, "Wrong polarity does not silently invert recipe");
        Check(StripeAnalysis.Analyze(new double[32], new double[32], r).Outcome == StripeOutcome.NoEdges, "Flat profile gives no edges");
        Reject(() => StripeAnalysis.Analyze(p[..31], p, r), "Exact profile length required");
        double[] invalid = (double[])p.Clone(); invalid[4] = double.NaN;
        Reject(() => StripeAnalysis.Analyze(p, invalid, r), "NaN profile rejected");
        p[8] = 0; Check(a.MeanProfile[8] == 220 && a.SmoothedProfile[8] == 220, "Profile snapshots independent of caller");
        try { ((IList<double>)a.SmoothedProfile)[0] = 1; throw new InvalidOperationException(); }
        catch (NotSupportedException) { Check(true, "Profiles read-only"); }
        double[] step = Enumerable.Range(0, 32).Select(i => i < 8 ? 20.0 : 220.0).ToArray();
        StripeReport one = StripeAnalysis.Analyze(step, step, r);
        Check(one.Outcome == StripeOutcome.NoPair && one.WidthPixels is null && one.FirstImagePoint is null, "Single edge not a zero-width measurement");
        double[] border = Enumerable.Range(0, 32).Select(i => i is >= 2 and < 20 ? 220.0 : 20.0).ToArray();
        Check(StripeAnalysis.Analyze(border, border, r).Outcome == StripeOutcome.BoundaryResponse, "Near-end response vetoes measurement");
        double[] many = Enumerable.Range(0, 32).Select(i => i is >= 6 and < 12 or >= 18 and < 24 ? 220.0 : 20.0).ToArray();
        StripeReport two = StripeAnalysis.Analyze(many, many, r);
        Check(two.Outcome == StripeOutcome.Ambiguous && two.Pairs.Count == 2 && two.WidthPixels is null, "Multiple stripes retained as ambiguous");
        double[] exact = Enumerable.Range(0, 32).Select(i => i is >= 8 and < 20 ? 40.0 : 20.0).ToArray();
        Check(StripeAnalysis.Analyze(exact, exact, r).WidthPixels == 12, "Derivative equals threshold is included");
        double[] ramp = new double[32]; for (int j = 8; j < 32; j++) ramp[j] = j == 8 ? 20 : 60;
        StripeReport group = StripeAnalysis.Analyze(ramp, ramp, r);
        Check(group.Edges.Count == 1 && Math.Abs(group.Edges[0].Position - (7.5 * 20 + 8.5 * 40) / 60) < 1e-12, "Adjacent same-sign differences form weighted location");
        Reject(() => r.ToImage(double.NaN), "Nonfinite coordinate mapping rejected");
        Reject(() => r.ToImage(32), "Coordinate mapping restricted to sample domain");
        double[] extra = Enumerable.Range(0, 32).Select(i => i is >= 6 and < 12 or >= 20 ? 220.0 : 20.0).ToArray();
        StripeReport unmatched = StripeAnalysis.Analyze(extra, extra, r);
        Check(unmatched.Outcome == StripeOutcome.Ambiguous && unmatched.Pairs.Count == 1 && unmatched.Edges.Count == 3 && unmatched.WidthPixels is null,
            "One plausible pair plus an extra unmatched edge remains ambiguous");
        double[] crowded = Enumerable.Range(0, 160).Select(i => i % 2 == 0 ? 20.0 : 220.0).ToArray();
        try { StripeAnalysis.Analyze(crowded, crowded, r with { Window = new(0, 0, 160, 8) }); throw new Exception("Capacity guard missing"); }
        catch (InvalidOperationException) { Check(true, "Too many edge groups fail explicitly, no truncation"); }
        Console.WriteLine($"Managed stripe checks passed: {count}. Synthetic profiles/recipe/pairing only; no OpenCV projection or smoothing.");
    }
}
