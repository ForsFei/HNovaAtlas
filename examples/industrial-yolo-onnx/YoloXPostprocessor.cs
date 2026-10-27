using System.Diagnostics.CodeAnalysis;

namespace IndustrialYolo.Onnx;

public readonly record struct YoloXGrid(int X, int Y, int Stride);
public sealed record PostprocessSettings(float ScoreThreshold = .1f, double NmsThreshold = .45);
public sealed record DecodedYoloXRow(int RowIndex, YoloXGrid Grid, Box NetworkBox,
    float Objectness, int ClassId, float ClassProbability, float Score);
public sealed record YoloXSuppression(int RowIndex, int KeptRowIndex, double Overlap);
public sealed record NoResizeSourceBox(Box? Original, bool WasClipped);
public sealed record YoloXDetection(DecodedYoloXRow Candidate, NoResizeSourceBox Source);
public sealed record PostprocessReport(PostprocessSettings Settings, int SourceHeight,
    IReadOnlyList<DecodedYoloXRow> Rows, IReadOnlyList<DecodedYoloXRow> Candidates,
    IReadOnlyList<YoloXDetection> Detections, IReadOnlyList<YoloXSuppression> Suppressions)
{
    public int Filtered => Rows.Count - Candidates.Count;
    public int Visible => Detections.Count(detection => detection.Source.Original is not null);
}

public static class YoloXPostprocessor
{
    public const int RowCount = 3549;
    public const int Columns = 85;

    public static YoloXGrid GridAt(int row)
    {
        Require(row is >= 0 and < RowCount, "Row index must be0..3548.");
        if (row < 2704) return new(row % 52, row / 52, 8);
        if (row < 3380) { int local = row - 2704; return new(local % 26, local / 26, 16); }
        int last = row - 3380;
        return new(last % 13, last / 13, 32);
    }

    public static PostprocessReport Process(YoloXRawOutput raw, YoloXInput input, PostprocessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(input);
        return ProcessValues(raw.Values, input.SourceHeight, settings);
    }

    // Also accepts explicitly handwritten arrays for offline tests, never used as native fallback.
    public static PostprocessReport ProcessValues(ReadOnlySpan<float> raw, int sourceHeight, PostprocessSettings settings)
    {
        Require(settings is not null && float.IsFinite(settings.ScoreThreshold) && settings.ScoreThreshold is >= 0 and <= 1 &&
            double.IsFinite(settings.NmsThreshold) && settings.NmsThreshold is >= 0 and <= 1, "Thresholds must be finite,0..1.");
        Require(sourceHeight is >= 1 and <= 416, "r=1 source height must be1..416.");
        Require(raw.Length == RowCount * Columns, "Only the fixed3549x85 raw layout is supported.");
        List<DecodedYoloXRow> rows = new(RowCount);
        for (int row = 0; row < RowCount; row++)
        {
            ReadOnlySpan<float> values = raw.Slice(row * Columns, Columns);
            for (int column = 0; column < Columns; column++)
            {
                Require(float.IsFinite(values[column]), $"Nonfinite raw value at row{row}.");
                if (column >= 4) Require(values[column] is >= 0 and <= 1, "Objectness/class outputs must already be probabilities; no implicit sigmoid.");
            }
            YoloXGrid grid = GridAt(row);
            float cx = (values[0] + grid.X) * grid.Stride;
            float cy = (values[1] + grid.Y) * grid.Stride;
            float width = MathF.Exp(values[2]) * grid.Stride;
            float height = MathF.Exp(values[3]) * grid.Stride;
            Box box = new(cx - width / 2f, cy - height / 2f, cx + width / 2f, cy + height / 2f);
            ValidateNetworkBox(box);
            float score = -1;
            int bestClass = 0;
            for (int classId = 0; classId < 80; classId++)
            {
                float product = values[4] * values[5 + classId];
                if (product > score) { score = product; bestClass = classId; }
            }
            rows.Add(new(row, grid, box, values[4], bestClass, values[5 + bestClass], score));
        }
        DecodedYoloXRow[] candidates = rows.Where(row => row.Score > settings.ScoreThreshold).ToArray();
        List<DecodedYoloXRow> kept = [];
        List<YoloXSuppression> suppressed = [];
        // Explicit deterministic tie rule; upstream NumPy default argsort does not promise this tie policy.
        foreach (DecodedYoloXRow candidate in candidates.OrderByDescending(row => row.Score).ThenBy(row => row.RowIndex))
        {
            YoloXSuppression? rejection = null;
            foreach (DecodedYoloXRow earlier in kept)
            {
                double overlap = UpstreamInclusiveNmsIou(candidate.NetworkBox, earlier.NetworkBox);
                if (overlap > settings.NmsThreshold)
                {
                    rejection = new(candidate.RowIndex, earlier.RowIndex, overlap);
                    break;
                }
            }
            if (rejection is not null) suppressed.Add(rejection);
            else kept.Add(candidate);
        }
        // Clip AFTER NMS; changing geometry before suppression changes its result.
        YoloXDetection[] detections = kept.Select(row => new YoloXDetection(row, ClipNoResize(row.NetworkBox, sourceHeight))).ToArray();
        return new(settings, sourceHeight, Array.AsReadOnly(rows.ToArray()), Array.AsReadOnly(candidates),
            Array.AsReadOnly(detections), Array.AsReadOnly(suppressed.ToArray()));
    }

    public static double UpstreamInclusiveNmsIou(Box first, Box second)
    {
        ValidateNetworkBox(first); ValidateNetworkBox(second);
        double intersection = Math.Max(0, Math.Min(first.X2, second.X2) - Math.Max(first.X1, second.X1) + 1) *
            Math.Max(0, Math.Min(first.Y2, second.Y2) - Math.Max(first.Y1, second.Y1) + 1);
        double areaA = (first.X2 - first.X1 + 1) * (first.Y2 - first.Y1 + 1);
        double areaB = (second.X2 - second.X1 + 1) * (second.Y2 - second.Y1 + 1);
        return intersection / (areaA + areaB - intersection);
    }

    public static NoResizeSourceBox ClipNoResize(Box network, int sourceHeight)
    {
        ValidateNetworkBox(network);
        Require(sourceHeight is >= 1 and <= 416, "Invalid r=1 source height.");
        double x1 = Math.Max(0, network.X1), y1 = Math.Max(0, network.Y1);
        double x2 = Math.Min(416, network.X2), y2 = Math.Min(sourceHeight, network.Y2);
        if (x2 <= x1 || y2 <= y1) return new(null, true);
        Box original = new(x1, y1, x2, y2);
        BoxMath.Validate(original, 416, sourceHeight);
        return new(original, original != network);
    }

    private static void ValidateNetworkBox(Box? box) => Require(box is not null &&
        double.IsFinite(box.X1) && double.IsFinite(box.Y1) && double.IsFinite(box.X2) && double.IsFinite(box.Y2) &&
        Math.Abs(box.X1) <= 1_000_000 && Math.Abs(box.Y1) <= 1_000_000 && Math.Abs(box.X2) <= 1_000_000 && Math.Abs(box.Y2) <= 1_000_000 &&
        box.X2 > box.X1 && box.Y2 > box.Y1 && (box.X2 - box.X1) * (box.Y2 - box.Y1) > 0,
        "Decoded box must be finite, positive, representable and within +/-1e6 teaching bound.");

    internal static void Require([DoesNotReturnIf(false)] bool condition, string message)
    { if (!condition) throw new ArgumentException(message); }
}
