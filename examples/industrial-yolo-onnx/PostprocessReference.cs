using System.Text.Json;
using System.Text.Json.Serialization;

namespace IndustrialYolo.Onnx;

internal sealed record PostprocessReferenceDocument(string ModelSha256, string InputSha256, int SourceHeight,
    double ScoreThreshold, double NmsThreshold, string RawSha256, string NumpyVersion,
    double[][] Rows, int[] CandidateRows, int[] KeptRows, double[]?[] SourceBoxes, bool[] Clipped);
public sealed record PostprocessComparison(int Rows, int Candidates, int Detections, double MaximumCoordinateError);

public static class PostprocessReference
{
    public static PostprocessComparison CompareFile(PostprocessReport actual, string inputSha256, string path)
    {
        ArgumentNullException.ThrowIfNull(actual);
        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        YoloXPostprocessor.Require(file.Length is > 0 and <= 2_097_152, "Reference JSON must be1..2MiB.");
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 16,
            RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        PostprocessReferenceDocument? reference = JsonSerializer.Deserialize<PostprocessReferenceDocument>(file, options);
        YoloXPostprocessor.Require(reference is not null && reference.ModelSha256 == ModelArtifact.Sha256 &&
            reference.InputSha256 == inputSha256 && reference.SourceHeight == actual.SourceHeight &&
            reference.ScoreThreshold == actual.Settings.ScoreThreshold && reference.NmsThreshold == actual.Settings.NmsThreshold,
            "Reference model/input/geometry/settings mismatch.");
        YoloXPostprocessor.Require(reference.RawSha256 is { Length: 64 } && reference.RawSha256.All(Uri.IsHexDigit) &&
            !string.IsNullOrWhiteSpace(reference.NumpyVersion) && reference.NumpyVersion.Length <= 64,
            "Reference provenance fields invalid (they are records, not trusted signatures).");
        YoloXPostprocessor.Require(reference.Rows is { Length: YoloXPostprocessor.RowCount } && reference.CandidateRows is not null &&
            reference.KeptRows is not null && reference.SourceBoxes is not null && reference.Clipped is not null,
            "Reference array fields invalid.");
        if (!reference.CandidateRows.SequenceEqual(actual.Candidates.Select(row => row.RowIndex)) ||
            !reference.KeptRows.SequenceEqual(actual.Detections.Select(detection => detection.Candidate.RowIndex)) ||
            reference.SourceBoxes.Length != actual.Detections.Count || reference.Clipped.Length != actual.Detections.Count)
            throw new InvalidOperationException("Reference candidate/kept order or mapped count mismatch.");
        double maximumError = 0;
        for (int row = 0; row < actual.Rows.Count; row++)
        {
            double[] expected = reference.Rows[row];
            YoloXPostprocessor.Require(expected is { Length: 6 }, "Each reference row must be[xyxy,classId,score].");
            DecodedYoloXRow observed = actual.Rows[row];
            CheckBox(observed.NetworkBox, expected.AsSpan(0, 4));
            if (expected[4] != observed.ClassId || !double.IsFinite(expected[5]) || expected[5] is < 0 or > 1 ||
                Math.Abs(expected[5] - observed.Score) > 1e-5 + 1e-4 * Math.Abs(expected[5]))
                throw new InvalidOperationException($"Reference class/score mismatch at row{row}.");
        }
        for (int i = 0; i < actual.Detections.Count; i++)
        {
            NoResizeSourceBox source = actual.Detections[i].Source;
            double[]? expected = reference.SourceBoxes[i];
            if ((source.Original is null) != (expected is null) || source.WasClipped != reference.Clipped[i])
                throw new InvalidOperationException("Reference source visibility/clipping mismatch.");
            if (source.Original is not null)
            {
                YoloXPostprocessor.Require(expected is { Length: 4 }, "Source reference must bexyxy or null.");
                CheckBox(source.Original, expected);
            }
        }
        return new(actual.Rows.Count, actual.Candidates.Count, actual.Detections.Count, maximumError);

        void CheckBox(Box box, ReadOnlySpan<double> expected)
        {
            double[] observed = [box.X1, box.Y1, box.X2, box.Y2];
            for (int i = 0; i < 4; i++)
            {
                double error = Math.Abs(observed[i] - expected[i]);
                if (!double.IsFinite(expected[i]) || error > 1e-4 + 2e-5 * Math.Abs(expected[i]))
                    throw new InvalidOperationException("Reference coordinate mismatch.");
                maximumError = Math.Max(maximumError, error);
            }
        }
    }
}
