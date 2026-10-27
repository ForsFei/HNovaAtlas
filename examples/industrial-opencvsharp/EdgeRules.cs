namespace IndustrialOpenCvSharp;

public sealed record EdgeRules(double Low = 50, double High = 150, int Aperture = 3,
    bool L2Gradient = true, int LineVotes = 20, double MinimumLength = 30, double MaximumGap = 3)
{
    public void Validate()
    {
        if (!double.IsFinite(Low) || !double.IsFinite(High) || Low < 0 || High <= Low || High > 10000 ||
            Aperture is not (3 or 5 or 7) || LineVotes is < 1 or > 10000 ||
            !double.IsFinite(MinimumLength) || MinimumLength is < 1 or > 4096 ||
            !double.IsFinite(MaximumGap) || MaximumGap is < 0 or > 128)
            throw new ArgumentException("Invalid teaching edge recipe.");
    }
}

public readonly record struct SamplePoint(double X, double Y);

public static class EdgeContracts
{
    public static SamplePoint[] Points(IReadOnlyList<SamplePoint> points, int minimum)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < minimum || points.Count > 1024) throw new ArgumentException("Point count outside teaching bounds.");
        SamplePoint[] copy = points.ToArray();
        if (copy.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) > 1000000 || Math.Abs(p.Y) > 1000000))
            throw new ArgumentException("Coordinates must be finite and bounded.");
        return copy;
    }

    public static void Checks()
    {
        int count = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); Console.WriteLine($"PASS {++count}: {label}"); }
        void Reject(Action action, string label)
        {
            try { action(); } catch (ArgumentException) { Check(true, label); return; }
            throw new InvalidOperationException(label);
        }
        new EdgeRules().Validate(); Check(true, "Default recipe valid; no native call");
        Reject(() => new EdgeRules(Low: -1).Validate(), "Negative low threshold rejected");
        Reject(() => new EdgeRules(High: 50).Validate(), "Equal thresholds rejected by teaching policy");
        Reject(() => new EdgeRules(High: double.NaN).Validate(), "NaN threshold rejected");
        Reject(() => new EdgeRules(High: 10001).Validate(), "High threshold bounded");
        Reject(() => new EdgeRules(Aperture: 4).Validate(), "Aperture whitelist");
        Reject(() => new EdgeRules(LineVotes: 0).Validate(), "Positive voting threshold");
        Reject(() => new EdgeRules(LineVotes: 10001).Validate(), "Voting threshold bounded");
        Reject(() => new EdgeRules(MinimumLength: 0).Validate(), "Positive segment length");
        Reject(() => new EdgeRules(MinimumLength: double.PositiveInfinity).Validate(), "Finite segment length");
        Reject(() => new EdgeRules(MaximumGap: -1).Validate(), "Nonnegative gap");
        Reject(() => new EdgeRules(MaximumGap: 129).Validate(), "Gap bounded");
        Reject(() => Points([], 2), "Insufficient line samples rejected");
        Reject(() => Points(new SamplePoint[1025], 6), "Point capacity bounded before native execution");
        Reject(() => Points([new(double.NaN, 0), new(0, 0)], 2), "Nonfinite sample rejected");
        Reject(() => Points([new(0, 0), new(1000001, 0)], 2), "Coordinate range bounded");
        SamplePoint[] input = [new(0, 0), new(1, 0)]; SamplePoint[] copied = Points(input, 2);
        input[0] = new(9, 9); Check(copied[0] == new SamplePoint(0, 0), "Point snapshot independent of input");
        new EdgeRules(Aperture: 7, MaximumGap: 0, L2Gradient: false).Validate(); Check(true, "Explicit alternate recipe accepted");
        Console.WriteLine($"Managed edge contracts passed: {count}. No Canny, Hough, fitting or quality validation executed.");
    }
}
