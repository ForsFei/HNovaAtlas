namespace IndustrialHalcon;

public static class EngineContracts
{
    public static void ValidateLow(double low)
    {
        if (!double.IsFinite(low) || low is < 0 or > 255) throw new ArgumentException("Finite byte threshold 0..255 required.");
    }
    public static BlobReport Read(double[] version, int count, double[] areas, double[] rows, double[] columns)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version.Length != 1 || version[0] != 1) throw new ArgumentException("Exactly one contract version, value 1 required.");
        return BlobReadback.FromArrays(8, 6, count, 2, 10, areas, rows, columns);
    }
    public static void Checks()
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
            throw new InvalidOperationException(label);
        }
        foreach (double value in new[] { 0.0, 200, 254, 255 }) { ValidateLow(value); Check(true, $"Threshold accepted: {value}"); }
        foreach (double value in new[] { -1.0, 256, double.NaN, double.PositiveInfinity })
            Reject(() => ValidateLow(value), "Invalid threshold refused before native call");
        double[] a = [4], r = [1.5], c = [1.5];
        BlobReport report = Read([1], 1, a, r, c);
        Check(report.Candidates.Single() == new BlobFeature(4, 1.5, 1.5), "Versioned output preserves pixel units");
        a[0] = r[0] = c[0] = 99;
        Check(report.Candidates[0] == new BlobFeature(4, 1.5, 1.5), "Returned values detached from tuple copies");
        Check(Read([1], 0, [], [], []).Outcome == BlobOutcome.NoCandidates, "Successful empty result stays empty, not failure or OK");
        Reject(() => Read([], 0, [], [], []), "Missing contract version refused");
        Reject(() => Read([1, 1], 0, [], [], []), "Nonscalar contract version refused");
        Reject(() => Read([2], 0, [], [], []), "Unsupported contract version refused");
        Reject(() => Read([double.NaN], 0, [], [], []), "Nonfinite contract version refused");
        Reject(() => Read([1], 1, [], [], []), "Iconic count cannot disagree with control tuples");
        Reject(() => Read([1], 0, [4], [1.5], [1.5]), "Zero object count cannot hide numeric candidates");
        Reject(() => Read([1], 1, [4], [], [1.5]), "Missing row tuple refused");
        Reject(() => Read([1], 1, [1], [1.5], [1.5]), "Output must satisfy selected area policy");
        Reject(() => Read([1], 1, [4], [6], [1.5]), "Fixed image coordinate envelope enforced");
        Console.WriteLine($"Engine managed checks passed: {count}. Contract only; no HDevelop parser, engine, algorithm or license invoked.");
    }
}
