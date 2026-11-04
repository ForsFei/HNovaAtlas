using IndustrialYolo;
using IndustrialYolo.Onnx;

namespace IndustrialYolo.Images;

public sealed record ImageCandidate(DecodedYoloXRow NetworkRow, Box ReferenceSource);
public sealed record ImageDetection(ImageCandidate Candidate, VisibleImageBox VisibleSource);
public sealed record ImageReport(PreparedImage Prepared, PostprocessSettings Settings,
    IReadOnlyList<DecodedYoloXRow> Rows, IReadOnlyList<ImageCandidate> Candidates,
    IReadOnlyList<ImageDetection> Detections, IReadOnlyList<YoloXSuppression> Suppressions)
{
    public int Visible => Detections.Count(d => d.VisibleSource.Original is not null);
}

public static class ImagePostprocessor
{
    public static ImageReport Process(YoloXRawOutput raw, PreparedImage prepared, PostprocessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return ProcessValues(raw.Values, prepared, settings);
    }

    public static ImageReport ProcessValues(ReadOnlySpan<float> raw, PreparedImage prepared, PostprocessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (settings is null || !float.IsFinite(settings.ScoreThreshold) || settings.ScoreThreshold is < 0 or > 1 ||
            !double.IsFinite(settings.NmsThreshold) || settings.NmsThreshold is < 0 or > 1)
            throw new ArgumentException("Finite0..1 score/NMS thresholds required.");
        IReadOnlyList<DecodedYoloXRow> rows = YoloXPostprocessor.DecodeValues(raw);
        ImageCandidate[] candidates = rows.Where(row => row.Score > settings.ScoreThreshold)
            .Select(row => new ImageCandidate(row, prepared.Geometry.ToReferenceSource(row.NetworkBox))).ToArray();
        List<ImageCandidate> kept = [];
        List<YoloXSuppression> suppressions = [];
        // +1 is not scale-invariant: the upstream script divides by ratio BEFORE NMS.
        foreach (ImageCandidate candidate in candidates.OrderByDescending(c => c.NetworkRow.Score).ThenBy(c => c.NetworkRow.RowIndex))
        {
            YoloXSuppression? rejection = null;
            foreach (ImageCandidate earlier in kept)
            {
                double overlap = YoloXPostprocessor.UpstreamInclusiveNmsIou(candidate.ReferenceSource, earlier.ReferenceSource);
                if (overlap > settings.NmsThreshold)
                {
                    rejection = new(candidate.NetworkRow.RowIndex, earlier.NetworkRow.RowIndex, overlap);
                    break;
                }
            }
            if (rejection is not null) suppressions.Add(rejection);
            else kept.Add(candidate);
        }
        ImageDetection[] detections = kept.Select(candidate => new ImageDetection(candidate,
            prepared.Geometry.ToVisibleSource(candidate.NetworkRow.NetworkBox))).ToArray();
        return new(prepared, settings, rows, Array.AsReadOnly(candidates), Array.AsReadOnly(detections), Array.AsReadOnly(suppressions.ToArray()));
    }
}
