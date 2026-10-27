using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace IndustrialYolo;

public enum DatasetSplit { Train, Validation, Test }
public sealed record Box(double X1, double Y1, double X2, double Y2);
public sealed record Truth(int ClassId, Box Box);
public sealed record DatasetImage(string Id, string Group, DatasetSplit Split,
    int Width, int Height, IReadOnlyList<Truth> Truth);
public sealed record Prediction(string ImageId, int ClassId, double Score, Box Box);
public sealed record EvaluationFixture(string FixtureKind, IReadOnlyList<string> Labels,
    IReadOnlyList<DatasetImage> Images, IReadOnlyList<Prediction> Predictions,
    double ScoreThreshold, double IouThreshold);
public enum Decision { TruePositive, FalsePositive, Filtered }
public sealed record PredictionDecision(int PredictionIndex, string ImageId,
    Decision Decision, int? TruthIndex, double? MatchedIou);
public sealed record EvaluationReport(int TruePositive, int FalsePositive, int FalseNegative,
    int Filtered, IReadOnlyList<PredictionDecision> Decisions)
{
    public double? Precision => TruePositive + FalsePositive == 0 ? null :
        (double)TruePositive / (TruePositive + FalsePositive);
    public double? Recall => TruePositive + FalseNegative == 0 ? null :
        (double)TruePositive / (TruePositive + FalseNegative);
}

// This schema is ours: original-image continuous xyxy, NOT a YOLO tensor/label format.
public static class FixtureJson
{
    public const int MaximumBytes = 1_048_576;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 16,
        Converters = { new JsonStringEnumConverter<DatasetSplit>(JsonNamingPolicy.CamelCase, false) }
    };

    public static AuditedDataset Read(string path)
    {
        using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(input.Length is > 0 and <= MaximumBytes, "JSON file must be 1..1048576 bytes.");
        return Audit(JsonSerializer.Deserialize<EvaluationFixture>(input, Options));
    }

    public static AuditedDataset Parse(string json)
    {
        Require(Encoding.UTF8.GetByteCount(json) is > 0 and <= MaximumBytes, "JSON size invalid.");
        return Audit(JsonSerializer.Deserialize<EvaluationFixture>(json, Options));
    }

    public static AuditedDataset Audit(EvaluationFixture? fixture) => AuditedDataset.Create(fixture);
    internal static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new ArgumentException(message);
    }
}

// Audit and detach collections at the boundary; later edits to caller lists cannot alter a run.
public sealed class AuditedDataset
{
    public IReadOnlyList<string> Labels { get; }
    public IReadOnlyList<DatasetImage> Images { get; }
    public IReadOnlyList<Prediction> Predictions { get; }
    public double ScoreThreshold { get; }
    public double IouThreshold { get; }

    private AuditedDataset(EvaluationFixture value)
    {
        Labels = Array.AsReadOnly(value.Labels.ToArray());
        Images = Array.AsReadOnly(value.Images.Select(image => image with
        { Truth = Array.AsReadOnly(image.Truth.ToArray()) }).ToArray());
        Predictions = Array.AsReadOnly(value.Predictions.ToArray());
        ScoreThreshold = value.ScoreThreshold;
        IouThreshold = value.IouThreshold;
    }

    internal static AuditedDataset Create(EvaluationFixture? fixture)
    {
        if (fixture is null) throw new ArgumentException("Fixture is null.");
        FixtureJson.Require(fixture.FixtureKind == "handwritten-evaluation", "Only handwritten-evaluation fixtures are supported; no inference is performed.");
        FixtureJson.Require(fixture.Labels is { Count: >= 1 and <= 64 }, "Need 1..64 labels.");
        HashSet<string> labels = new(StringComparer.Ordinal);
        foreach (string label in fixture.Labels)
            FixtureJson.Require(ValidText(label) && labels.Add(label), "Labels must be nonblank, trimmed, unique and <=64 chars.");
        FixtureJson.Require(fixture.Images is { Count: >= 1 and <= 256 }, "Need 1..256 images.");
        FixtureJson.Require(fixture.Predictions is { Count: <= 4096 }, "At most 4096 predictions.");
        FixtureJson.Require(double.IsFinite(fixture.ScoreThreshold) && fixture.ScoreThreshold is >= 0 and <= 1,
            "Score threshold must be finite, 0..1.");
        FixtureJson.Require(double.IsFinite(fixture.IouThreshold) && fixture.IouThreshold is > 0 and <= 1,
            "IoU threshold must be finite, (0,1].");
        Dictionary<string, DatasetImage> images = new(StringComparer.Ordinal);
        Dictionary<string, DatasetSplit> groups = new(StringComparer.Ordinal);
        HashSet<DatasetSplit> splits = [];
        foreach (DatasetImage image in fixture.Images)
        {
            FixtureJson.Require(image is not null, "Image is null.");
            FixtureJson.Require(ValidText(image.Id) && images.TryAdd(image.Id, image), "Image id invalid or duplicated.");
            FixtureJson.Require(ValidText(image.Group) && Enum.IsDefined(image.Split), "Group/split invalid.");
            FixtureJson.Require(image.Width is > 0 and <= 8192 && image.Height is > 0 and <= 8192 &&
                (long)image.Width * image.Height <= 16_777_216, "Image dimensions exceed teaching limits.");
            FixtureJson.Require(!groups.TryGetValue(image.Group, out DatasetSplit previous) || previous == image.Split,
                "Same physical group cannot cross dataset splits.");
            groups[image.Group] = image.Split;
            splits.Add(image.Split);
            FixtureJson.Require(image.Truth is { Count: <= 128 }, "At most 128 truth boxes per image.");
            foreach (Truth truth in image.Truth)
            {
                FixtureJson.Require(truth is not null && truth.ClassId >= 0 && truth.ClassId < fixture.Labels.Count,
                    "Truth class invalid.");
                BoxMath.Validate(truth.Box, image.Width, image.Height);
            }
        }
        FixtureJson.Require(splits.Count == 3, "This teaching manifest requires train, validation and test splits.");
        Dictionary<string, int> perImage = new(StringComparer.Ordinal);
        foreach (Prediction prediction in fixture.Predictions)
        {
            FixtureJson.Require(prediction is not null && ValidText(prediction.ImageId), "Prediction is null or image id invalid.");
            if (!images.TryGetValue(prediction.ImageId, out DatasetImage? image) || image.Split != DatasetSplit.Test)
                throw new ArgumentException("Predictions must reference known test images only.");
            FixtureJson.Require(prediction.ClassId >= 0 && prediction.ClassId < fixture.Labels.Count, "Prediction class invalid.");
            FixtureJson.Require(double.IsFinite(prediction.Score) && prediction.Score is >= 0 and <= 1, "Score invalid.");
            BoxMath.Validate(prediction.Box, image.Width, image.Height);
            int count = perImage.GetValueOrDefault(image.Id) + 1;
            FixtureJson.Require(count <= 256, "At most 256 predictions per image.");
            perImage[image.Id] = count;
        }
        return new AuditedDataset(fixture);
    }

    private static bool ValidText(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 64 && value == value.Trim() && !value.Any(char.IsControl);
}

public static class BoxMath
{
    public static void Validate(Box? box, int width = 8192, int height = 8192)
    {
        FixtureJson.Require(box is not null, "Box is null.");
        FixtureJson.Require(double.IsFinite(box.X1) && double.IsFinite(box.Y1) &&
            double.IsFinite(box.X2) && double.IsFinite(box.Y2) && box.X1 >= 0 && box.Y1 >= 0 &&
            box.X2 <= width && box.Y2 <= height && box.X2 > box.X1 && box.Y2 > box.Y1 &&
            double.IsFinite(Area(box)) && Area(box) > 0, "Box must be finite, positive-area and within image edges.");
    }

    public static double IoU(Box first, Box second)
    {
        Validate(first);
        Validate(second);
        double width = Math.Max(0, Math.Min(first.X2, second.X2) - Math.Max(first.X1, second.X1));
        double height = Math.Max(0, Math.Min(first.Y2, second.Y2) - Math.Max(first.Y1, second.Y1));
        double intersection = width * height;
        return Math.Clamp(intersection / (Area(first) + Area(second) - intersection), 0, 1);
    }

    private static double Area(Box box) => (box.X2 - box.X1) * (box.Y2 - box.Y1);
}

public static class DetectionEvaluator
{
    // One fixed score/IoU operating point; NOT COCO evaluation, AP, mAP or NMS.
    public static EvaluationReport Evaluate(AuditedDataset dataset)
    {
        int tp = 0, fp = 0, fn = 0, filtered = 0;
        List<PredictionDecision> decisions = [];
        foreach (DatasetImage image in dataset.Images.Where(image => image.Split == DatasetSplit.Test))
        {
            bool[] used = new bool[image.Truth.Count];
            var ordered = dataset.Predictions.Select((prediction, index) => (prediction, index))
                .Where(item => item.prediction.ImageId == image.Id)
                .OrderByDescending(item => item.prediction.Score).ThenBy(item => item.index);
            foreach (var (prediction, index) in ordered)
            {
                if (prediction.Score < dataset.ScoreThreshold)
                {
                    filtered++;
                    decisions.Add(new(index, image.Id, Decision.Filtered, null, null));
                    continue;
                }
                int best = -1;
                double bestIou = -1;
                for (int i = 0; i < image.Truth.Count; i++)
                {
                    if (used[i] || image.Truth[i].ClassId != prediction.ClassId) continue;
                    double iou = BoxMath.IoU(prediction.Box, image.Truth[i].Box);
                    if (iou > bestIou) { bestIou = iou; best = i; }
                }
                if (best >= 0 && bestIou >= dataset.IouThreshold)
                {
                    used[best] = true;
                    tp++;
                    decisions.Add(new(index, image.Id, Decision.TruePositive, best, bestIou));
                }
                else
                {
                    fp++;
                    decisions.Add(new(index, image.Id, Decision.FalsePositive, null, null));
                }
            }
            fn += used.Count(matched => !matched);
        }
        return new(tp, fp, fn, filtered, Array.AsReadOnly(decisions.ToArray()));
    }
}
