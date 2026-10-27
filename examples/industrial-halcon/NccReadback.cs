namespace IndustrialHalcon;

public enum NccOutcome { NoMatch, Candidates }
// Raw HALCON matching pose, NOT an exact transformed pixel point or a mechanical pose.
public sealed record NccCandidate(double RawRowPixels, double RawColumnPixels, double AngleRadians, double Score);
public sealed record NccReport(NccOutcome Outcome, IReadOnlyList<NccCandidate> Candidates);

public static class NccReadback
{
    public static NccReport FromArrays(double minimumScore, int maximumMatches,
        double[] rows, double[] columns, double[] angles, double[] scores)
    {
        ArgumentNullException.ThrowIfNull(rows); ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(angles); ArgumentNullException.ThrowIfNull(scores);
        if (!double.IsFinite(minimumScore) || minimumScore is < 0 or > 1 || maximumMatches is < 1 or > 8)
            throw new ArgumentException("Finite NCC score interval and teaching result cap 1..8 required.");
        int n = scores.Length;
        if (n > maximumMatches || rows.Length != n || columns.Length != n || angles.Length != n)
            throw new ArgumentException("All four output tuple lengths must agree and stay within the requested cap.");
        NccCandidate[] result = new NccCandidate[n];
        for (int i = 0; i < n; i++)
        {
            // This adapter supports ONLY the fixed 64x64, zero-origin-offset, zero-rotation fixture.
            // General HALCON origins / rotated poses may not satisfy these restrictions.
            if (!double.IsFinite(rows[i]) || rows[i] is < 0 or > 63 ||
                !double.IsFinite(columns[i]) || columns[i] is < 0 or > 63 ||
                !double.IsFinite(angles[i]) || Math.Abs(angles[i]) > 1e-8 ||
                !double.IsFinite(scores[i]) || scores[i] < minimumScore || scores[i] > 1)
                throw new ArgumentException("Output violates the fixed translation-only fixture contract.");
            result[i] = new(rows[i], columns[i], angles[i], scores[i]);
        }
        return new(n == 0 ? NccOutcome.NoMatch : NccOutcome.Candidates, Array.AsReadOnly(result));
    }
}
