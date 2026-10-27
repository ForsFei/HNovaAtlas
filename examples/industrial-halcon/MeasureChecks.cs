namespace IndustrialHalcon;

public static class MeasureChecks
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
        MeasureWindow w = new(); w.Validate();
        Check(w.Project(48, 47.5) == (-16.5, 0), "Phi zero scans horizontally toward increasing columns");
        MeasureWindow vertical = w with { Phi = Math.PI / 2 }; vertical.Validate();
        var vp = vertical.Project(63.5, 64);
        Check(Math.Abs(vp.Along + 15.5) < 1e-10 && Math.Abs(vp.Across) < 1e-10, "Positive pi/2 scan advances upward, not toward increasing rows");
        Reject(() => (w with { Phi = double.NaN }).Validate(), "Nonfinite angle refused");
        Reject(() => (w with { Phi = 4 }).Validate(), "Teaching angle envelope enforced");
        Reject(() => (w with { HalfLength1 = 3 }).Validate(), "Too short teaching profile refused");
        Reject(() => (w with { HalfLength2 = 0 }).Validate(), "Zero averaging half-width refused");
        Reject(() => (w with { HalfLength1 = double.PositiveInfinity }).Validate(), "Nonfinite half-length refused");
        Reject(() => (w with { Sigma = .3 }).Validate(), "Sigma below SDK lower bound refused");
        Reject(() => (w with { Sigma = 11 }).Validate(), "Teaching sigma/profile ratio bound enforced");
        Reject(() => (w with { Threshold = 0 }).Validate(), "Nonpositive amplitude threshold refused by teaching policy");
        Reject(() => (w with { Threshold = double.NaN }).Validate(), "Nonfinite threshold refused");
        Reject(() => (w with { Row = 8 }).Validate(), "Bilinear sampling margin is required beyond the rectangle");
        Reject(() => (w with { Column = 40 }).Validate(), "Window touching column zero refused, no clamping");
        Reject(() => (w with { Phi = Math.PI / 4, Row = 20 }).Validate(), "Rotated extents checked, not just window center");
        Reject(() => MeasureContracts.Transition("all"), "SDK all mode intentionally not accepted by this fixed first-polarity adapter");
        Reject(() => MeasureContracts.Transition("positve"), "Mistyped transition refused");
        EdgeProfile Edges(double[] r, double[] c, double[] a, double[] d) => MeasureContracts.ReadEdges(w, r, c, a, d);
        PairProfile Pair(double[] c1, double[] c2, double[] intra, double[] inter) => MeasureContracts.ReadPairs(w, "positive",
            Enumerable.Repeat(48.0, c1.Length).ToArray(), c1, Enumerable.Repeat(80.0, c1.Length).ToArray(),
            Enumerable.Repeat(48.0, c2.Length).ToArray(), c2, Enumerable.Repeat(-80.0, c2.Length).ToArray(), intra, inter);
        EdgeProfile none = Edges([], [], [], []); PairProfile noPairs = Pair([], [], [], []);
        MeasureReport blank = MeasureContracts.Combine(none, noPairs);
        Check(blank.Outcome == MeasureOutcome.NoEdges && blank.WidthPixels is null, "Empty successful output has no width, not zero width or product OK");
        EdgeProfile single = Edges([48], [63.5], [80], []);
        Check(MeasureContracts.Combine(single, noPairs).Outcome == MeasureOutcome.NoPair, "One edge is incomplete width evidence");
        double[] r = [48, 48], c = [47.5, 79.5], a = [80, -80], d = [32];
        EdgeProfile edges = Edges(r, c, a, d); PairProfile pair = Pair([47.5], [79.5], [32], []);
        MeasureReport report = MeasureContracts.Combine(edges, pair);
        Check(report.Outcome == MeasureOutcome.WidthMeasured && report.WidthPixels == 32, "One ordered complete pair with exactly two edges yields pixel width");
        r[0] = c[0] = a[0] = d[0] = 999;
        Check(edges.Edges[0].Position == new ImagePixel(48, 47.5) && edges.AdjacentDistancesPixels[0] == 32,
            "Edge arrays and distance storage copied independently");
        try { ((IList<double>)edges.AdjacentDistancesPixels)[0] = 1; throw new InvalidOperationException("Read-only distances expected"); }
        catch (NotSupportedException) { Check(true, "Published edge distances are read-only"); }
        try { ((IList<MeasuredPair>)pair.Pairs)[0] = new(edges.Edges[0], edges.Edges[1], 1); throw new InvalidOperationException("Read-only pairs expected"); }
        catch (NotSupportedException) { Check(true, "Published edge pairs are read-only"); }
        Reject(() => Edges([48, 48], [47.5, 79.5], [80, -80], [32, 0]), "N edges require N-1 distances, not N");
        Reject(() => Edges([48], [], [80], []), "Edge coordinate length mismatch refused");
        Reject(() => Edges([48], [63.5], [], []), "Amplitude length mismatch refused");
        Reject(() => Edges([double.NaN], [63.5], [80], []), "Nonfinite edge position refused");
        Reject(() => Edges([48], [63.5], [double.NaN], []), "Nonfinite amplitude refused");
        Reject(() => Edges([48], [63.5], [19], []), "Below-threshold amplitude refused");
        Reject(() => Edges([49], [63.5], [80], []), "Returned coordinate must be on profile axis, not anywhere in rectangle");
        Reject(() => Edges([48], [105], [80], []), "Edge outside major-axis extent refused");
        Reject(() => Edges([48, 48], [79.5, 47.5], [-80, 80], [32]), "Order is scan order, not blindly sorted later");
        Reject(() => Edges([48, 48], [47.5, 79.5], [80, -80], [31]), "Distance must agree with returned coordinates");
        Reject(() => Edges([48, 48], [47.5, 79.5], [80, -80], [double.NaN]), "Nonfinite adjacent distance refused");
        Reject(() => Edges(null!, [], [], []), "Missing edge tuple refused");
        Reject(() => Edges(Enumerable.Repeat(48.0, 17).ToArray(), [], [], []), "Sixteen-edge teaching cap enforced");
        Reject(() => Pair([47.5], [79.5], [32], [32]), "One pair has no inter-pair gap tuple element");
        Reject(() => Pair([47.5], [79.5], [], []), "Intra tuple length must equal pair count");
        Reject(() => Pair([47.5], [79.5], [31], []), "Intra distance cannot be confused with endpoint-index difference");
        Reject(() => Pair([79.5], [47.5], [32], []), "Reversed pair endpoints refused");
        Reject(() => MeasureContracts.ReadPairs(w, "positive", [48], [47.5], [-80], [48], [79.5], [80], [32], []),
            "Pair signs must agree with positive first transition");
        EdgeProfile dark = Edges([48, 48], [47.5, 79.5], [-80, 80], [32]);
        PairProfile darkPair = MeasureContracts.ReadPairs(w, "negative", [48], [47.5], [-80], [48], [79.5], [80], [32], []);
        Check(MeasureContracts.Combine(dark, darkPair).WidthPixels == 32, "Dark strip uses negative first transition without changing width unit");
        Check(MeasureContracts.Combine(edges, noPairs).Outcome == MeasureOutcome.NoPair, "Two detected edges do not force a pair under wrong polarity");
        EdgeProfile four = Edges([48, 48, 48, 48], [31.5, 47.5, 71.5, 87.5], [80, -80, 80, -80], [16, 24, 16]);
        PairProfile two = Pair([31.5, 71.5], [47.5, 87.5], [16, 16], [24]);
        MeasureReport ambiguous = MeasureContracts.Combine(four, two);
        Check(ambiguous.Outcome == MeasureOutcome.Ambiguous && ambiguous.WidthPixels is null && two.InterDistancesPixels[0] == 24,
            "Two intra widths are distinct from one inter gap; do not auto-pick a product width");
        Check(MeasureContracts.Combine(four, Pair([31.5], [47.5], [16], [])).Outcome == MeasureOutcome.Ambiguous,
            "One returned pair plus extra edges is not automatically accepted");
        Reject(() => Pair([31.5, 71.5], [47.5, 87.5], [16, 16], [16]), "Inter gap checked against end-of-first to start-of-next");
        Reject(() => MeasureContracts.Combine(none, pair), "Pairs cannot exist without observed edges");
        Reject(() => MeasureContracts.Combine(edges, Pair([48], [80], [32], [])), "Different pair endpoints cannot be attached to prior edge snapshot");
        EdgeProfile ve = MeasureContracts.ReadEdges(vertical, [63.5, 31.5], [64, 64], [80, -80], [32]);
        PairProfile vpair = MeasureContracts.ReadPairs(vertical, "positive", [63.5], [64], [80], [31.5], [64], [-80], [32], []);
        Check(MeasureContracts.Combine(ve, vpair).WidthPixels == 32, "Descending rows are correct increasing scan coordinates at pi/2");
        int[] bright = MeasureFixture.Pixels(MeasureScene.BrightStrip), inverse = MeasureFixture.Pixels(MeasureScene.DarkStrip);
        Check(bright.Length == 12288 && bright.Count(v => v == 220) == 2048, "Original strip has 64x32 bright pixels on a 128x96 image");
        Check(bright.Zip(inverse).All(p => p.First + p.Second == 236), "Dark fixture is a true gray-value inversion, not a forced negative result");
        Check(bright[48 * 128 + 47] == 16 && bright[48 * 128 + 48] == 220 && bright[48 * 128 + 79] == 220 && bright[48 * 128 + 80] == 16,
            "Thirty-two bright centers are bounded by ideal transitions at 47.5 and 79.5, not width 31");
        Check(MeasureFixture.Pixels(MeasureScene.TwoStrips).Count(v => v == 220) == 2048, "Two 16-pixel strips retain distinct geometry despite same bright count");
        Check(MeasureFixture.Pixels(MeasureScene.HorizontalStrip).Count(v => v == 220) == 3072, "Horizontal fixture dimensions differ explicitly from vertical one");
        Check(MeasureFixture.Pixels(MeasureScene.Blank).All(v => v == 16), "Blank scene is actual uniform pixels, not a fake HALCON response");
        int[] step = MeasureFixture.Pixels(MeasureScene.SingleStep);
        Check(step[48 * 128 + 63] == 16 && step[48 * 128 + 64] == 220 && step[48 * 128 + 104] == 220, "Single step has no second edge inside the window");
        Reject(() => MeasureFixture.Pixels((MeasureScene)99), "Unknown fixture scene refused");
        Console.WriteLine($"Measurement managed checks passed: {count}. Window/output/pixel contracts only; no edge detection, Gaussian filtering or HALCON native calls.");
    }
}
