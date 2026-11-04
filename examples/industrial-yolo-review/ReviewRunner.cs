using System.Runtime.InteropServices;
using System.Text.Json;
using IndustrialYolo.Images;
using IndustrialYolo.Onnx;
using OpenCvSharp;

namespace IndustrialYolo.Review;

public sealed record ImageReview(ReviewCase Metadata, string InputSha256, double Ratio, int ResizedWidth, int ResizedHeight,
    IReadOnlyList<IndustrialYolo.Truth> Truth, CaseEvaluation Evaluation);
public sealed record ClassReview(int ClassId, Counts Counts);
public sealed record GroupReview(string Group, Counts Counts, IReadOnlyList<string> ImageIds);
public sealed record ReviewReport(string ReportVersion, DateTimeOffset StartedUtc, string ManifestSha256,
    string ModelSha256, string PipelineVersion, string LabelSet, string DatasetVersion, string AnnotationVersion,
    string DatasetKind, string Partition, float ScoreThreshold, double NmsThreshold, double MatchIouThreshold,
    string OrtVersion, string OpenCvVersion, string Framework, string Architecture,
    string MatchingPolicy, string VisibilityPolicy, Counts Total, IReadOnlyList<ClassReview> Classes,
    IReadOnlyList<GroupReview> Groups, IReadOnlyList<ImageReview> Images);

public static class ReviewRunner
{
    public static ReviewReport Run(LoadedReview review, string model)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        ReviewManifest manifest = review.Manifest;
        List<ImageReview> images = [];
        HashSet<string> imageHashes = new(StringComparer.OrdinalIgnoreCase);
        using YoloXNanoSession session = new(model); // ONE actual session for the whole sequential batch.
        foreach (LoadedCase item in review.Cases)
        {
            LoadedImage image = OpenCvImageInput.Load(item.ImagePath);
            ReviewData.Require(string.Equals(image.EncodedSha256, item.Metadata.ImageSha256, StringComparison.OrdinalIgnoreCase) &&
                image.Bgr.Width == item.Metadata.Width && image.Bgr.Height == item.Metadata.Height, "Decoded image size/hash disagrees with manifest.");
            ReviewData.Require(imageHashes.Add(image.EncodedSha256), "Duplicate encoded image content in evaluation batch.");
            PreparedImage prepared = OpenCvImageInput.Prepare(image);
            YoloXRawOutput raw = session.Run(prepared.Tensor);
            ImageReport output = ImagePostprocessor.Process(raw, prepared, new(manifest.ScoreThreshold, manifest.NmsThreshold));
            ReviewPrediction[] predictions = output.Detections.Select(d => new ReviewPrediction(d.Candidate.NetworkRow.RowIndex,
                d.Candidate.NetworkRow.ClassId, d.Candidate.NetworkRow.Score, d.VisibleSource.Original, d.VisibleSource.WasClipped)).ToArray();
            CaseEvaluation evaluation = Matching.Evaluate(image.Bgr.Width, image.Bgr.Height, item.Truth, predictions, manifest.MatchIouThreshold);
            images.Add(new(item.Metadata, prepared.Tensor.Sha256, prepared.Geometry.Ratio,
                prepared.Geometry.ResizedWidth, prepared.Geometry.ResizedHeight, item.Truth, evaluation));
        }
        Counts total = Sum(images.Select(i => i.Evaluation.Counts));
        ClassReview[] classes = Enumerable.Range(0, 80).Select(c => new ClassReview(c, new Counts(
            images.Sum(i => i.Evaluation.Decisions.Count(d => d.Decision == "TP" && d.Prediction.ClassId == c)),
            images.Sum(i => i.Evaluation.Decisions.Count(d => d.Decision == "FP" && d.Prediction.ClassId == c)),
            images.Sum(i => i.Evaluation.MissedTruth.Count(n => i.Truth[n].ClassId == c))))).ToArray();
        GroupReview[] groups = images.GroupBy(i => i.Metadata.Group, StringComparer.Ordinal).Select(g =>
            new GroupReview(g.Key, Sum(g.Select(i => i.Evaluation.Counts)), Array.AsReadOnly(g.Select(i => i.Metadata.Id).ToArray()))).ToArray();
        return new("atlas-yolo-report-v1", started, review.ManifestSha256, ModelArtifact.Sha256, ReviewData.Pipeline,
            ReviewData.LabelSet, manifest.DatasetVersion, manifest.AnnotationVersion, manifest.DatasetKind, manifest.Partition,
            manifest.ScoreThreshold, manifest.NmsThreshold, manifest.MatchIouThreshold, session.RuntimeVersion,
            Cv2.GetVersionString() ?? throw new InvalidOperationException("OpenCV runtime version unavailable."),
            RuntimeInformation.FrameworkDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            "score>threshold; class-aware greedy score-desc,row-asc; IoU>=match; continuous area; not COCO AP/mAP",
            "source-space +1 class-agnostic NMS; clip after NMS to actual resized content; empty visible area excluded and recorded",
            total, Array.AsReadOnly(classes), Array.AsReadOnly(groups), Array.AsReadOnly(images.ToArray()));
    }
    private static Counts Sum(IEnumerable<Counts> counts)
    { Counts[] all = counts.ToArray(); return new(all.Sum(c => c.Tp), all.Sum(c => c.Fp), all.Sum(c => c.Fn)); }
    public static void Publish(ReviewReport report, string destination)
    {
        destination = Path.GetFullPath(destination);
        if (File.Exists(destination)) throw new IOException("Report already exists; choose a new output path.");
        string directory = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Report parent folder must already exist.");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(report, ReviewData.Json);
        ReviewData.Require(bytes.Length <= 33_554_432, "Report exceeds32MiB teaching boundary.");
        string temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        bool created = false;
        try
        {
            using (FileStream file = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { created = true; file.Write(bytes); file.Flush(flushToDisk: true); }
            File.Move(temporary, destination, overwrite: false);
        }
        finally { if (created && File.Exists(temporary)) File.Delete(temporary); }
    }
}
