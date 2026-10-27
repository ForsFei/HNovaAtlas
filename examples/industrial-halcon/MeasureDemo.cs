using HalconDotNet;

namespace IndustrialHalcon;

public static class MeasureDemo
{
    private static double[] Numbers(HTuple tuple)
    {
        using HTuple real = tuple.TupleReal();
        return BlobReadback.CopyUsedValues(real.DArr, real.Length);
    }
    public static MeasureReport Analyze(MeasureScene scene, MeasureWindow window, string transition)
    {
        ArgumentNullException.ThrowIfNull(window); window.Validate(); MeasureContracts.Transition(transition);
        int[] pixels = MeasureFixture.Pixels(scene);
        using HTuple rr = new(Enumerable.Range(0, pixels.Length).Select(i => i / 128).ToArray());
        using HTuple cc = new(Enumerable.Range(0, pixels.Length).Select(i => i % 128).ToArray());
        using HTuple gray = new(pixels);
        HObject? image = null;
        HTuple? handle = null, rows = null, columns = null, amplitudes = null, distances = null;
        HTuple? r1 = null, c1 = null, a1 = null, r2 = null, c2 = null, a2 = null, intra = null, inter = null;
        try
        {
            HOperatorSet.GenImageConst(out image, "byte", 128, 96);
            HOperatorSet.SetGrayval(image, rr, cc, gray); // Only this new owned image; no shared buffer or aliases.
            HOperatorSet.GenMeasureRectangle2(window.Row, window.Column, window.Phi, window.HalfLength1, window.HalfLength2,
                128, 96, "bilinear", out handle);
            HOperatorSet.MeasurePos(image, handle, window.Sigma, window.Threshold, "all", "all",
                out rows, out columns, out amplitudes, out distances);
            EdgeProfile edges = MeasureContracts.ReadEdges(window, Numbers(rows), Numbers(columns), Numbers(amplitudes), Numbers(distances));
            HOperatorSet.MeasurePairs(image, handle, window.Sigma, window.Threshold, transition, "all",
                out r1, out c1, out a1, out r2, out c2, out a2, out intra, out inter);
            PairProfile pairs = MeasureContracts.ReadPairs(window, transition,
                Numbers(r1), Numbers(c1), Numbers(a1), Numbers(r2), Numbers(c2), Numbers(a2), Numbers(intra), Numbers(inter));
            return MeasureContracts.Combine(edges, pairs);
        }
        finally
        {
            inter?.Dispose(); intra?.Dispose(); a2?.Dispose(); c2?.Dispose(); r2?.Dispose();
            a1?.Dispose(); c1?.Dispose(); r1?.Dispose(); distances?.Dispose(); amplitudes?.Dispose(); columns?.Dispose(); rows?.Dispose();
            // Fixed package HTuple.Dispose releases contained handle ownership. No borrowed handle / forced CloseMeasure / cache.
            handle?.Dispose(); image?.Dispose();
        }
    }
    private static void ExpectedWidth(MeasureReport report, bool vertical)
    {
        if (report.Outcome != MeasureOutcome.WidthMeasured || report.EdgeProfile.Edges.Count != 2 ||
            report.WidthPixels is not { } width || Math.Abs(width - 32) > 0.75)
            throw new InvalidOperationException("Expected one synthetic 32-pixel edge pair.");
        MeasuredPair pair = report.PairProfile.Pairs[0];
        double first = vertical ? pair.First.Position.Row : pair.First.Position.Column;
        double second = vertical ? pair.Second.Position.Row : pair.Second.Position.Column;
        if (Math.Abs(first - (vertical ? 63.5 : 47.5)) > .75 || Math.Abs(second - (vertical ? 31.5 : 79.5)) > .75)
            throw new InvalidOperationException("Synthetic boundary position/direction expectation differs.");
    }
    public static void Run(bool checkExpected)
    {
        MeasureWindow window = new();
        MeasureReport bright = Analyze(MeasureScene.BrightStrip, window, "positive");
        Console.WriteLine($"Native 1D measurement: outcome={bright.Outcome}, edges={bright.EdgeProfile.Edges.Count}, pairs={bright.PairProfile.Pairs.Count}, width={bright.WidthPixels?.ToString() ?? "none"} px; not millimeters or product OK/NG.");
        foreach (MeasuredEdge edge in bright.EdgeProfile.Edges)
            Console.WriteLine($"row={edge.Position.Row}, column={edge.Position.Column}, signed amplitude={edge.Amplitude}");
        if (!checkExpected) return;
        ExpectedWidth(bright, false);
        ExpectedWidth(Analyze(MeasureScene.DarkStrip, window, "negative"), false);
        ExpectedWidth(Analyze(MeasureScene.HorizontalStrip, window with { Phi = Math.PI / 2 }, "positive"), true);
        MeasureReport blank = Analyze(MeasureScene.Blank, window, "positive");
        if (blank.Outcome != MeasureOutcome.NoEdges || blank.WidthPixels is not null)
            throw new InvalidOperationException("Blank image cannot become zero-width product.");
        MeasureReport step = Analyze(MeasureScene.SingleStep, window, "positive");
        if (step.Outcome != MeasureOutcome.NoPair || step.EdgeProfile.Edges.Count != 1 || step.WidthPixels is not null)
            throw new InvalidOperationException("One edge is not a complete width.");
        MeasureReport wrong = Analyze(MeasureScene.BrightStrip, window, "negative");
        if (wrong.Outcome != MeasureOutcome.NoPair || wrong.EdgeProfile.Edges.Count != 2 || wrong.WidthPixels is not null)
            throw new InvalidOperationException("Opposite first-transition policy must not invent a pair.");
        MeasureReport two = Analyze(MeasureScene.TwoStrips, window, "positive");
        if (two.Outcome != MeasureOutcome.Ambiguous || two.EdgeProfile.Edges.Count != 4 || two.PairProfile.Pairs.Count != 2 ||
            two.WidthPixels is not null || two.PairProfile.Pairs.Any(p => Math.Abs(p.WidthPixels - 16) > .75) ||
            two.PairProfile.InterDistancesPixels.Count != 1 || Math.Abs(two.PairProfile.InterDistancesPixels[0] - 24) > .75)
            throw new InvalidOperationException("Two strips must preserve two widths and one separate inter-pair gap.");
        Console.WriteLine("Seven actual HALCON 1D measurement fixture cases passed. Not calibrated accuracy/production acceptance.");
    }
}
