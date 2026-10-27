namespace IndustrialHalcon;

public static class ContractChecks
{
    private static int count;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine($"PASS {++count}: {label}");
    }
    private static void Reject<T>(Action action, string label) where T : Exception
    {
        try { action(); } catch (T) { Check(true, label); return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}: {label}");
    }
    public static void Run()
    {
        count = 0;
        double[] capacity = [4, 4, 999];
        double[] used = BlobReadback.CopyUsedValues(capacity, 2);
        Check(used.Length == 2 && used.SequenceEqual(new double[] { 4, 4 }), "Only used tuple prefix is copied, not spare internal array capacity");
        capacity[0] = 99;
        Check(used[0] == 4 && BlobReadback.CopyUsedValues(capacity, 0).Length == 0, "Tuple numeric snapshot is independent and supports empty prefix");
        Reject<ArgumentOutOfRangeException>(() => BlobReadback.CopyUsedValues(capacity, 4), "Used tuple length cannot exceed storage");
        Reject<ArgumentOutOfRangeException>(() => BlobReadback.CopyUsedValues(capacity, -1), "Negative used tuple length refused");
        BlobReport Read(int n, double[] a, double[] r, double[] c) => BlobReadback.FromArrays(8, 6, n, 2, 10, a, r, c);
        BlobReport empty = Read(0, [], [], []);
        Check(empty.Outcome == BlobOutcome.NoCandidates && empty.Candidates.Count == 0, "Validated zero objects yields NoCandidates, not an exception or product OK");
        double[] areas = [4], rows = [1.5], columns = [1.5];
        BlobReport one = Read(1, areas, rows, columns);
        Check(one.Outcome == BlobOutcome.Candidates && one.Candidates[0] == new BlobFeature(4, 1.5, 1.5), "Plain numeric synthetic candidate copies fractional row/column, area remains pixels");
        Check(one.ImageWidth == 8 && one.ImageHeight == 6, "Output retains source dimensions");
        areas[0] = 10; rows[0] = 4; columns[0] = 6;
        Check(one.Candidates[0] == new BlobFeature(4, 1.5, 1.5), "Mutating adapter arrays cannot change published result");
        Reject<NotSupportedException>(() => ((IList<BlobFeature>)one.Candidates)[0] = new(2, 0, 0), "Published list is read-only");
        BlobReport two = Read(2, [4, 4], [1.5, 1.5], [5.5, 1.5]);
        Check(two.Candidates[0].ColumnPixels == 5.5 && two.Candidates[1].ColumnPixels == 1.5, "Input order retained, no promise that first component is leftmost or primary");
        Check(Read(1, [2], [0], [0]).Candidates.Count == 1 && Read(1, [10], [5], [7]).Candidates.Count == 1, "Area and image-center bounds are inclusive");
        Reject<ArgumentException>(() => Read(1, [], [], []), "Iconic count one cannot become empty numeric output");
        Reject<ArgumentException>(() => Read(0, [0], [0], [0]), "One empty region/zero area cannot be confused with no objects");
        Reject<ArgumentException>(() => Read(1, [4], [], [1]), "Row tuple length mismatch refused");
        Reject<ArgumentException>(() => Read(1, [4], [1], [1, 2]), "Column tuple length mismatch refused");
        Reject<ArgumentException>(() => Read(-1, [], [], []), "Negative reported count refused");
        Reject<ArgumentException>(() => Read(65, [], [], []), "Output count cap enforced before allocation");
        Reject<ArgumentException>(() => Read(1, [0], [0], [0]), "Zero-area region is not a selected positive-area candidate");
        Reject<ArgumentException>(() => Read(1, [1], [1], [1]), "Below selected area interval refused");
        Reject<ArgumentException>(() => Read(1, [11], [1], [1]), "Above selected area interval refused");
        Reject<ArgumentException>(() => Read(1, [4.5], [1], [1]), "Fractional pixel count refused");
        Reject<ArgumentException>(() => Read(1, [double.NaN], [1], [1]), "Nonfinite area refused");
        Reject<ArgumentException>(() => Read(1, [4], [-.1], [1]), "Negative row refused, not clamped");
        Reject<ArgumentException>(() => Read(1, [4], [6], [1]), "Row outside original image refused");
        Reject<ArgumentException>(() => Read(1, [4], [1], [8]), "Column outside original image refused");
        Reject<ArgumentException>(() => Read(1, [4], [double.PositiveInfinity], [1]), "Nonfinite row refused");
        Reject<ArgumentException>(() => Read(1, [4], [1], [double.NaN]), "Nonfinite column refused");
        Reject<ArgumentNullException>(() => Read(0, null!, [], []), "Missing numeric array refused");
        Reject<ArgumentException>(() => BlobReadback.FromArrays(0, 6, 0, 2, 10, [], [], []), "Invalid image geometry refused");
        Reject<ArgumentException>(() => BlobReadback.FromArrays(2000, 2000, 0, 2, 10, [], [], []), "Teaching image resource cap enforced");
        Reject<ArgumentException>(() => BlobReadback.FromArrays(8, 6, 0, 0, 10, [], [], []), "Positive selection minimum required");
        Reject<ArgumentException>(() => BlobReadback.FromArrays(8, 6, 0, 10, 2, [], [], []), "Reversed area interval refused");
        Reject<ArgumentException>(() => BlobReadback.FromArrays(8, 6, 0, 2, 49, [], [], []), "Area cannot exceed image pixel count");
        Reject<ArgumentException>(() => BlobReadback.FromArrays(8, 6, 0, double.NaN, 10, [], [], []), "Nonfinite area contract refused");
        Console.WriteLine($"Managed contract checks passed: {count}. No HALCON operator/native runtime/license was invoked; not segmentation validation.");
    }
}
