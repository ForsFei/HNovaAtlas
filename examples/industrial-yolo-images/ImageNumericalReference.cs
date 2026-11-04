// 一般图片的逐元素跨语言对照。
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialYolo;
using IndustrialYolo.Onnx;

namespace IndustrialYolo.Images;

internal sealed record ImageReferenceDocument(string ModelSha256, string EncodedSha256, string InputSha256,
    int Width, int Height, double Ratio, int ResizedWidth, int ResizedHeight,
    string OpenCvVersion, string NumpyVersion, string OrtVersion, double ScoreThreshold, double NmsThreshold,
    double[][] Rows, int[] CandidateRows, int[] KeptRows, double[]?[] SourceBoxes, bool[] Clipped);

public sealed record ImageComparison(int InputValues, NumericComparison Raw, int Rows, int Candidates,
    int Kept, double MaximumCoordinateError);

// Cross-language numerical comparison.
public static class ImageReference
{
    public static ImageComparison Compare(PreparedImage prepared, YoloXRawOutput raw, ImageReport report, string prefix)
    {
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Little-endian reference required.");
        using (FileStream input = new(prefix + ".input.f32", FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Require(input.Length == prepared.Tensor.Values.Length * sizeof(float), "Input reference length mismatch.");
            byte[] bytes = new byte[checked((int)input.Length)];
            input.ReadExactly(bytes);
            Require(MemoryMarshal.Cast<byte, float>(bytes).SequenceEqual(prepared.Tensor.Values), "Input tensor must match exactly.");
        }
        NumericComparison rawComparison = NumericReference.CompareFile(raw, prefix + ".output.f32");
        using FileStream file = new(prefix + ".json", FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(file.Length is > 0 and <= 2_097_152, "Reference JSON must be1..2MiB.");
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 16,
            RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        ImageReferenceDocument reference = JsonSerializer.Deserialize<ImageReferenceDocument>(file, options)
            ?? throw new InvalidOperationException("Null reference.");
        ImageGeometry geometry = prepared.Geometry;
        Require(reference.ModelSha256 == ModelArtifact.Sha256 && reference.EncodedSha256 == prepared.Source.EncodedSha256 &&
            reference.InputSha256 == prepared.Tensor.Sha256 && reference.Width == geometry.Width && reference.Height == geometry.Height &&
            reference.Ratio == geometry.Ratio && reference.ResizedWidth == geometry.ResizedWidth && reference.ResizedHeight == geometry.ResizedHeight &&
            reference.ScoreThreshold == report.Settings.ScoreThreshold && reference.NmsThreshold == report.Settings.NmsThreshold,
            "Reference model/image/tensor/geometry/settings mismatch.");
        Require(reference.OpenCvVersion == "4.13.0" && reference.OrtVersion == "1.27.0" &&
            !string.IsNullOrWhiteSpace(reference.NumpyVersion), "Reference runtime provenance mismatch.");
        Require(reference.Rows is { Length: 3549 } && reference.CandidateRows is not null && reference.KeptRows is not null &&
            reference.SourceBoxes is not null && reference.Clipped is not null, "Invalid reference arrays.");
        Require(reference.CandidateRows!.SequenceEqual(report.Candidates.Select(c => c.NetworkRow.RowIndex)) &&
            reference.KeptRows!.SequenceEqual(report.Detections.Select(d => d.Candidate.NetworkRow.RowIndex)) &&
            reference.SourceBoxes!.Length == report.Detections.Count && reference.Clipped!.Length == report.Detections.Count,
            "Candidate/kept order mismatch.");
        double maximum = 0;
        for (int i = 0; i < report.Rows.Count; i++)
        {
            double[] expected = reference.Rows![i];
            Require(expected is { Length: 6 }, "Reference row must be[xyxy,classId,score].");
            DecodedYoloXRow observed = report.Rows[i];
            CheckBox(geometry.ToReferenceSource(observed.NetworkBox), expected.AsSpan(0, 4));
            Require(expected[4] == observed.ClassId && double.IsFinite(expected[5]) &&
                Math.Abs(expected[5] - observed.Score) <= 1e-5 + 1e-4 * Math.Abs(expected[5]), "Class/score mismatch.");
        }
        for (int i = 0; i < report.Detections.Count; i++)
        {
            VisibleImageBox observed = report.Detections[i].VisibleSource;
            double[]? expected = reference.SourceBoxes![i];
            Require((observed.Original is null) == (expected is null) && observed.WasClipped == reference.Clipped![i], "Visibility/clipping mismatch.");
            if (observed.Original is not null)
            {
                Require(expected is { Length: 4 }, "Visible box must bexyxy.");
                CheckBox(observed.Original, expected!);
            }
        }
        return new(prepared.Tensor.Values.Length, rawComparison, report.Rows.Count, report.Candidates.Count, report.Detections.Count, maximum);

        void CheckBox(Box box, ReadOnlySpan<double> expected)
        {
            double[] actual = [box.X1, box.Y1, box.X2, box.Y2];
            for (int i = 0; i < 4; i++)
            {
                double error = Math.Abs(actual[i] - expected[i]);
                Require(double.IsFinite(expected[i]) && error <= 1e-4 + 2e-5 * Math.Abs(expected[i]), "Coordinate mismatch.");
                maximum = Math.Max(maximum, error);
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
