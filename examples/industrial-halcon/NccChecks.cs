namespace IndustrialHalcon;

public static class NccChecks
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
            throw new InvalidOperationException($"Expected invalid input: {label}");
        }
        NccReport Read(double[] r, double[] c, double[] a, double[] s) => NccReadback.FromArrays(.99, 2, r, c, a, s);
        NccReport empty = Read([], [], [], []);
        Check(empty.Outcome == NccOutcome.NoMatch && empty.Candidates.Count == 0, "Four empty tuples represent completed no-match, not product OK");
        double[] rows = [41.5], columns = [39.5], angles = [0], scores = [1];
        NccReport single = Read(rows, columns, angles, scores);
        Check(single.Outcome == NccOutcome.Candidates && single.Candidates[0] == new NccCandidate(41.5, 39.5, 0, 1), "Fractional raw matching pose retained with radians and score");
        rows[0] = columns[0] = angles[0] = scores[0] = 99;
        Check(single.Candidates[0] == new NccCandidate(41.5, 39.5, 0, 1), "Result owns independent numeric snapshot");
        try { ((IList<NccCandidate>)single.Candidates)[0] = new(0, 0, 0, 1); throw new InvalidOperationException("Read-only expected"); }
        catch (NotSupportedException) { Check(true, "Published candidates are read-only"); }
        NccReport two = Read([42, 20], [40, 30], [0, 0], [.99, 1]);
        Check(two.Candidates.Count == 2 && two.Candidates[0].RawRowPixels == 42, "Two candidates retained in output order, not auto-chosen or re-sorted");
        Reject(() => Read([1], [], [0], [1]), "Column length mismatch refused");
        Reject(() => Read([1], [1], [], [1]), "Angle length mismatch refused");
        Reject(() => Read([1], [1], [0], []), "Empty scores cannot hide populated coordinates");
        Reject(() => Read([], [], [], [1]), "Scores cannot create a default origin candidate");
        Reject(() => Read([1, 2, 3], [1, 2, 3], [0, 0, 0], [1, 1, 1]), "Requested result cap enforced");
        Reject(() => NccReadback.FromArrays(.99, 0, [], [], [], []), "HALCON unlimited NumMatches=0 excluded by teaching output policy");
        Reject(() => NccReadback.FromArrays(.99, 9, [], [], [], []), "Teaching maximum count eight enforced");
        Reject(() => NccReadback.FromArrays(double.NaN, 2, [], [], [], []), "Nonfinite score threshold refused");
        Reject(() => NccReadback.FromArrays(-.1, 2, [], [], [], []), "Negative score threshold refused");
        Reject(() => NccReadback.FromArrays(1.1, 2, [], [], [], []), "Score threshold above one refused");
        Reject(() => Read([1], [1], [0], [.98]), "Score below selected threshold refused");
        Reject(() => Read([1], [1], [0], [1.01]), "Score above one refused, not clamped");
        Reject(() => Read([1], [1], [0], [double.NaN]), "Nonfinite score refused");
        Reject(() => Read([double.NaN], [1], [0], [1]), "Nonfinite row refused");
        Reject(() => Read([1], [double.PositiveInfinity], [0], [1]), "Nonfinite column refused");
        Reject(() => Read([-1], [1], [0], [1]), "Negative coordinate refused only by this fixed-origin fixture policy");
        Reject(() => Read([1], [64], [0], [1]), "Outside fixed 64x64 fixture refused");
        Reject(() => Read([1], [1], [double.NaN], [1]), "Nonfinite angle refused");
        Reject(() => Read([1], [1], [.01], [1]), "Rotated result refused by translation-only adapter");
        Reject(() => Read(null!, [], [], []), "Missing output array refused");
        Check(Read([0], [63], [0], [.99]).Candidates.Count == 1, "Fixed-fixture numeric endpoints accepted, not proof a template fits there");
        int[] template = NccFixture.Pixels(0, 0), shifted = NccFixture.Pixels(18, 14);
        Check(template.Length == 4096 && shifted.Length == 4096 && template.All(v => v is >= 0 and <= 255), "Original deterministic image data fits 64x64 byte contract");
        Check(template.Count(v => v != 16) == 768 && shifted.Count(v => v != 16) == 768, "Exactly 24x32 patterned pixels on each image");
        Check(template[0] == 16 && template[12 * 64 + 10] == 32 && template[35 * 64 + 41] != 16, "Background and inclusive patch endpoints match definition");
        bool identical = true;
        for (int r = 12; r <= 35; r++) for (int c = 10; c <= 41; c++)
            identical &= template[r * 64 + c] == shifted[(r + 18) * 64 + c + 14];
        Check(identical, "Pure pixel translation preserves patch; this does not compute NCC");
        Check(template.Where(v => v != 16).Distinct().Count() > 100, "Template has varied gray texture, not a constant patch");
        Check(NccFixture.TemplateRoi.Area == 768, "Model domain pixel area is not a handle or match count");
        Reject(() => NccFixture.Pixels(int.MaxValue, 0), "Shift overflow/out-of-image rejected before indexing");
        Reject(() => NccFixture.Pixels(0, -11), "Negative shifted patch column rejected");
        Check(!ReferenceEquals(template, NccFixture.Pixels(0, 0)), "Each synthetic image owns its array");
        Console.WriteLine($"NCC managed checks passed: {count}. Fixture/output contracts only; no correlation search, HALCON native call or license validation.");
    }
}
