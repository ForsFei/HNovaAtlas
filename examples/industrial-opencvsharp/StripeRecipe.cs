namespace IndustrialOpenCvSharp;

public enum ScanAxis { X, Y }
public enum StripePolarity { Bright, Dark }
public readonly record struct ScanWindow(int X, int Y, int Width, int Height);
public sealed record StripeRecipe(ScanWindow Window, ScanAxis Axis = ScanAxis.X, bool Reverse = false,
    StripePolarity Polarity = StripePolarity.Bright, double DifferenceThreshold = 20, double Sigma = 1)
{
    public const int Guard = 3;
    public int Length => Axis == ScanAxis.X ? Window.Width : Window.Height;
    public void Validate()
    {
        if (!Enum.IsDefined(Axis) || !Enum.IsDefined(Polarity) || Window.X is < 0 or > 1048576 || Window.Y is < 0 or > 1048576 ||
            Window.Width < 1 || Window.Height < 1 || Length is < 16 or > 4096 ||
            (Axis == ScanAxis.X ? Window.Height : Window.Width) > 256 ||
            !double.IsFinite(DifferenceThreshold) || DifferenceThreshold is <= 0 or > 255 ||
            !double.IsFinite(Sigma) || Sigma is < .25 or > 2)
            throw new ArgumentException("Invalid axis-aligned stripe teaching recipe.");
    }
    public void ValidateImage(int width, int height)
    {
        Validate();
        if (width < 1 || height < 1 || (long)width * height > 1048576 ||
            (long)Window.X + Window.Width > width || (long)Window.Y + Window.Height > height)
            throw new ArgumentException("ROI must be fully inside the bounded input image; no clipping.");
    }
    public SamplePoint ToImage(double scanPosition)
    {
        Validate();
        if (!double.IsFinite(scanPosition) || scanPosition < 0 || scanPosition > Length - 1)
            throw new ArgumentException("Position must lie inside the scan profile.");
        double local = Reverse ? Length - 1 - scanPosition : scanPosition;
        return Axis == ScanAxis.X
            ? new(Window.X + local, Window.Y + (Window.Height - 1) / 2.0)
            : new(Window.X + (Window.Width - 1) / 2.0, Window.Y + local);
    }
}

public enum StripeOutcome { NoEdges, NoPair, Ambiguous, BoundaryResponse, WidthMeasured }
public sealed record ScanEdge(double Position, double SignedPeak, int FirstDifference, int LastDifference);
public sealed record StripePair(ScanEdge First, ScanEdge Second, double WidthPixels);
public sealed record StripeReport(StripeRecipe Recipe, StripeOutcome Outcome,
    IReadOnlyList<double> MeanProfile, IReadOnlyList<double> SmoothedProfile,
    IReadOnlyList<ScanEdge> Edges, IReadOnlyList<StripePair> Pairs,
    double? WidthPixels, SamplePoint? FirstImagePoint, SamplePoint? SecondImagePoint);

public static class StripeAnalysis
{
    // Inputs are positive-axis profiles. This method does NOT perform native projection or Gaussian smoothing.
    public static StripeReport Analyze(IReadOnlyList<double> mean, IReadOnlyList<double> smooth, StripeRecipe recipe)
    {
        recipe.Validate();
        double[] Snapshot(IReadOnlyList<double> source)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source.Count != recipe.Length) throw new ArgumentException("Profile length must equal scan dimension.");
            double[] copy = source.ToArray();
            if (copy.Any(v => !double.IsFinite(v) || v is < -1e-8 or > 255.00000001))
                throw new ArgumentException("Expected finite Mono8-derived profile.");
            if (recipe.Reverse) Array.Reverse(copy);
            return copy;
        }
        double[] raw = Snapshot(mean), p = Snapshot(smooth);
        List<ScanEdge> edges = [];
        int i = 0;
        while (i < p.Length - 1)
        {
            double difference = p[i + 1] - p[i];
            if (Math.Abs(difference) < recipe.DifferenceThreshold) { i++; continue; }
            int first = i, sign = Math.Sign(difference);
            double weight = 0, weightedPosition = 0, peak = difference;
            while (i < p.Length - 1)
            {
                double value = p[i + 1] - p[i];
                if (Math.Abs(value) < recipe.DifferenceThreshold || Math.Sign(value) != sign) break;
                double amplitude = Math.Abs(value);
                weight += amplitude; weightedPosition += (i + .5) * amplitude;
                if (amplitude > Math.Abs(peak)) peak = value;
                i++;
            }
            edges.Add(new(weightedPosition / weight, peak, first, i - 1));
            if (edges.Count > 64) throw new InvalidOperationException("Too many edge groups; no silent truncation.");
        }
        int firstSign = recipe.Polarity == StripePolarity.Bright ? 1 : -1;
        List<StripePair> pairs = [];
        for (int j = 0; j + 1 < edges.Count; j++)
            if (Math.Sign(edges[j].SignedPeak) == firstSign && Math.Sign(edges[j + 1].SignedPeak) == -firstSign)
                pairs.Add(new(edges[j], edges[j + 1], edges[j + 1].Position - edges[j].Position));
        bool boundary = edges.Any(e => e.FirstDifference < StripeRecipe.Guard || e.LastDifference >= p.Length - 1 - StripeRecipe.Guard);
        StripeOutcome outcome = boundary ? StripeOutcome.BoundaryResponse : edges.Count == 0 ? StripeOutcome.NoEdges :
            pairs.Count == 0 ? StripeOutcome.NoPair : edges.Count != 2 || pairs.Count != 1 ? StripeOutcome.Ambiguous : StripeOutcome.WidthMeasured;
        StripePair? selected = outcome == StripeOutcome.WidthMeasured ? pairs[0] : null;
        return new(recipe, outcome, Array.AsReadOnly(raw), Array.AsReadOnly(p), edges.AsReadOnly(), pairs.AsReadOnly(),
            selected?.WidthPixels, selected is null ? null : recipe.ToImage(selected.First.Position),
            selected is null ? null : recipe.ToImage(selected.Second.Position));
    }
}
