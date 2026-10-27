namespace IndustrialHalcon;

public sealed record MeasureWindow(int Row = 48, int Column = 64, double Phi = 0,
    double HalfLength1 = 40, double HalfLength2 = 8, double Sigma = 1, double Threshold = 20)
{
    public void Validate()
    {
        if (!double.IsFinite(Phi) || Math.Abs(Phi) > Math.PI ||
            !double.IsFinite(HalfLength1) || HalfLength1 is < 4 or > 56 ||
            !double.IsFinite(HalfLength2) || HalfLength2 is < 1 or > 16 ||
            !double.IsFinite(Sigma) || Sigma < 0.4 || Sigma > Math.Min(10, HalfLength1 / 4) ||
            !double.IsFinite(Threshold) || Threshold is < 1 or > 255)
            throw new ArgumentException("Bounded teaching window, sigma and amplitude threshold required.");
        double dr = Math.Abs(Math.Sin(Phi)) * HalfLength1 + Math.Abs(Math.Cos(Phi)) * HalfLength2;
        double dc = Math.Abs(Math.Cos(Phi)) * HalfLength1 + Math.Abs(Math.Sin(Phi)) * HalfLength2;
        // Fixed 128x96 byte input, integer center (HALCON rounds centers), bilinear interpolation + 1px margin.
        if (Row - dr < 1 || Row + dr > 94 || Column - dc < 1 || Column + dc > 126)
            throw new ArgumentException("The entire window plus bilinear margin must fit the fixed image; never clamp.");
    }
    public (double Along, double Across) Project(double row, double column)
    {
        double r = row - Row, c = column - Column;
        return (-Math.Sin(Phi) * r + Math.Cos(Phi) * c, Math.Cos(Phi) * r + Math.Sin(Phi) * c);
    }
}

public sealed record MeasuredEdge(ImagePixel Position, double Amplitude);
public sealed record MeasuredPair(MeasuredEdge First, MeasuredEdge Second, double WidthPixels);
public sealed record EdgeProfile(IReadOnlyList<MeasuredEdge> Edges, IReadOnlyList<double> AdjacentDistancesPixels);
public sealed record PairProfile(IReadOnlyList<MeasuredPair> Pairs, IReadOnlyList<double> InterDistancesPixels);
public enum MeasureOutcome { NoEdges, NoPair, Ambiguous, WidthMeasured }
public sealed record MeasureReport(MeasureOutcome Outcome, EdgeProfile EdgeProfile, PairProfile PairProfile, double? WidthPixels);

public static class MeasureContracts
{
    private const double Tolerance = 1e-4;
    public static void Transition(string transition)
    {
        if (transition is not "positive" and not "negative")
            throw new ArgumentException("This teaching pair adapter supports positive or negative first transition only.");
    }
    private static MeasuredEdge Edge(MeasureWindow w, double row, double column, double amplitude)
    {
        if (!double.IsFinite(row) || !double.IsFinite(column) || !double.IsFinite(amplitude) ||
            row is < 0 or > 95 || column is < 0 or > 127 || Math.Abs(amplitude) + Tolerance < w.Threshold)
            throw new ArgumentException("Finite in-image edge with sufficient signed amplitude required.");
        var p = w.Project(row, column);
        if (Math.Abs(p.Across) > Tolerance || Math.Abs(p.Along) > w.HalfLength1 + Tolerance)
            throw new ArgumentException("Returned edge must lie on the fixed measure axis and within its extent.");
        return new(new(row, column), amplitude);
    }
    private static double Along(MeasureWindow w, MeasuredEdge edge) => w.Project(edge.Position.Row, edge.Position.Column).Along;
    private static void Distance(double actual, double expected)
    {
        if (!double.IsFinite(actual) || actual <= 0 || expected <= 0 || Math.Abs(actual - expected) > Tolerance)
            throw new ArgumentException("Positive ordered rectangle-axis distance must agree with returned coordinates.");
    }
    public static EdgeProfile ReadEdges(MeasureWindow w, double[] rows, double[] columns, double[] amplitudes, double[] distances)
    {
        ArgumentNullException.ThrowIfNull(w); w.Validate();
        ArgumentNullException.ThrowIfNull(rows); ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(amplitudes); ArgumentNullException.ThrowIfNull(distances);
        int n = rows.Length;
        if (n > 16 || columns.Length != n || amplitudes.Length != n || distances.Length != Math.Max(0, n - 1))
            throw new ArgumentException("N edge positions/amplitudes and max(0,N-1) distances required, with teaching cap 16.");
        MeasuredEdge[] edges = new MeasuredEdge[n];
        for (int i = 0; i < n; i++)
        {
            edges[i] = Edge(w, rows[i], columns[i], amplitudes[i]);
            if (i > 0) Distance(distances[i - 1], Along(w, edges[i]) - Along(w, edges[i - 1]));
        }
        return new(Array.AsReadOnly(edges), Array.AsReadOnly(distances.ToArray()));
    }
    public static PairProfile ReadPairs(MeasureWindow w, string transition,
        double[] rows1, double[] columns1, double[] amplitudes1, double[] rows2, double[] columns2, double[] amplitudes2,
        double[] intra, double[] inter)
    {
        ArgumentNullException.ThrowIfNull(w); w.Validate(); Transition(transition);
        ArgumentNullException.ThrowIfNull(rows1); ArgumentNullException.ThrowIfNull(columns1); ArgumentNullException.ThrowIfNull(amplitudes1);
        ArgumentNullException.ThrowIfNull(rows2); ArgumentNullException.ThrowIfNull(columns2); ArgumentNullException.ThrowIfNull(amplitudes2);
        ArgumentNullException.ThrowIfNull(intra); ArgumentNullException.ThrowIfNull(inter);
        int n = rows1.Length;
        if (n > 8 || columns1.Length != n || amplitudes1.Length != n || rows2.Length != n || columns2.Length != n ||
            amplitudes2.Length != n || intra.Length != n || inter.Length != Math.Max(0, n - 1))
            throw new ArgumentException("Seven equally sized pair arrays and max(0,M-1) inter distances required, cap eight pairs.");
        MeasuredPair[] pairs = new MeasuredPair[n];
        for (int i = 0; i < n; i++)
        {
            MeasuredEdge first = Edge(w, rows1[i], columns1[i], amplitudes1[i]);
            MeasuredEdge second = Edge(w, rows2[i], columns2[i], amplitudes2[i]);
            if ((first.Amplitude > 0) != (transition == "positive") || (second.Amplitude > 0) == (transition == "positive"))
                throw new ArgumentException("Pair polarity must agree with the requested first transition.");
            Distance(intra[i], Along(w, second) - Along(w, first));
            pairs[i] = new(first, second, intra[i]);
            if (i > 0) Distance(inter[i - 1], Along(w, first) - Along(w, pairs[i - 1].Second));
        }
        return new(Array.AsReadOnly(pairs), Array.AsReadOnly(inter.ToArray()));
    }
    public static MeasureReport Combine(EdgeProfile edges, PairProfile pairs)
    {
        ArgumentNullException.ThrowIfNull(edges); ArgumentNullException.ThrowIfNull(pairs);
        // Both native calls use the same image/window/sigma/threshold and select=all. Pair endpoints must be observed edges.
        bool Same(MeasuredEdge a, MeasuredEdge b) => Math.Abs(a.Position.Row - b.Position.Row) <= Tolerance &&
            Math.Abs(a.Position.Column - b.Position.Column) <= Tolerance && Math.Abs(a.Amplitude - b.Amplitude) <= Tolerance;
        if (pairs.Pairs.Count * 2 > edges.Edges.Count || pairs.Pairs.Any(p =>
            !edges.Edges.Any(e => Same(e, p.First)) || !edges.Edges.Any(e => Same(e, p.Second))))
            throw new ArgumentException("Pair endpoints do not agree with the all-edge readback.");
        MeasureOutcome outcome = edges.Edges.Count == 0 ? MeasureOutcome.NoEdges : pairs.Pairs.Count == 0 ? MeasureOutcome.NoPair :
            pairs.Pairs.Count == 1 && edges.Edges.Count == 2 ? MeasureOutcome.WidthMeasured : MeasureOutcome.Ambiguous;
        return new(outcome, edges, pairs, outcome == MeasureOutcome.WidthMeasured ? pairs.Pairs[0].WidthPixels : null);
    }
}
