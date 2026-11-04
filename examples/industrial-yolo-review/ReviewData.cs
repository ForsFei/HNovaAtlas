using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialYolo;
using IndustrialYolo.Images;
using IndustrialYolo.Onnx;

namespace IndustrialYolo.Review;

public sealed record ReviewCase(string Id, string Group, string Image, string Labels, int Width, int Height,
    string ImageSha256, string LabelsSha256);
public sealed record ReviewManifest(string SchemaVersion, string DatasetVersion, string AnnotationVersion,
    string DatasetKind, string Partition, string LabelSet, string ModelSha256, string PipelineVersion,
    float ScoreThreshold, double NmsThreshold, double MatchIouThreshold, IReadOnlyList<ReviewCase> Cases);
public sealed record LoadedCase(ReviewCase Metadata, string ImagePath, IReadOnlyList<Truth> Truth);
public sealed class LoadedReview
{
    public ReviewManifest Manifest { get; }
    public string ManifestSha256 { get; }
    public IReadOnlyList<LoadedCase> Cases { get; }
    internal LoadedReview(ReviewManifest manifest, string manifestSha256, IReadOnlyList<LoadedCase> cases)
    { Manifest = manifest; ManifestSha256 = manifestSha256; Cases = cases; }
}

public static class ReviewData
{
    public const string Schema = "atlas-yolo-review-v1", Pipeline = "atlas-yolox-image-v1", LabelSet = "coco80-yolox";
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = 24,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true
    };
    public static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static byte[] ReadBytes(string path, int maximum)
    {
        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(file.Length >= 0 && file.Length <= maximum, "File exceeds teaching size limit.");
        byte[] bytes = new byte[(int)file.Length]; file.ReadExactly(bytes); return bytes;
    }
    public static LoadedReview Load(string path)
    {
        path = Path.GetFullPath(path); string root = Path.GetDirectoryName(path)!;
        byte[] bytes = ReadBytes(path, 1_048_576);
        ReviewManifest manifest = JsonSerializer.Deserialize<ReviewManifest>(bytes, Json) ?? throw new ArgumentException("Null manifest.");
        Require(manifest.SchemaVersion == Schema && manifest.LabelSet == LabelSet && manifest.PipelineVersion == Pipeline &&
            string.Equals(manifest.ModelSha256, ModelArtifact.Sha256, StringComparison.OrdinalIgnoreCase), "Only the pinned YOLOX/COCO80 image pipeline is supported.");
        Require(Text(manifest.DatasetVersion) && Text(manifest.AnnotationVersion) &&
            manifest.DatasetKind is "synthetic-check" or "user-supplied" && manifest.Partition is "validation" or "test", "Dataset metadata invalid.");
        Require(float.IsFinite(manifest.ScoreThreshold) && manifest.ScoreThreshold is >= 0 and <= 1 &&
            double.IsFinite(manifest.NmsThreshold) && manifest.NmsThreshold is >= 0 and <= 1 &&
            double.IsFinite(manifest.MatchIouThreshold) && manifest.MatchIouThreshold is > 0 and <= 1, "Invalid thresholds.");
        Require(manifest.Cases is { Count: >= 1 and <= 32 }, "Need1..32 explicitly listed cases.");
        HashSet<string> ids = new(StringComparer.Ordinal), files = new(StringComparer.OrdinalIgnoreCase);
        List<LoadedCase> loaded = [];
        foreach (ReviewCase item in manifest.Cases!)
        {
            Require(item is not null && Text(item.Id) && ids.Add(item.Id) && Text(item.Group), "Invalid/duplicate image id or group.");
            ImageGeometry.ValidateDimensions(item!.Width, item.Height);
            Require(Hash(item.ImageSha256) && Hash(item.LabelsSha256), "Expected64 hexadecimal SHA256 fields.");
            string image = Resolve(root, item.Image), labels = Resolve(root, item.Labels);
            Require(files.Add(image) && files.Add(labels), "Image/label paths must be distinct across cases.");
            Require(File.Exists(image), "Image missing.");
            byte[] annotation = ReadBytes(labels, 131_072); // Missing label is an error; empty is an explicit negative.
            Require(string.Equals(Sha(annotation), item.LabelsSha256, StringComparison.OrdinalIgnoreCase), "Annotation bytes disagree with manifest hash.");
            loaded.Add(new(item, image, ParseLabels(annotation, item.Width, item.Height)));
        }
        // Capture a private metadata copy as well as private truth arrays.
        ReviewManifest snapshot = manifest with { Cases = Array.AsReadOnly(manifest.Cases!.ToArray()) };
        return new(snapshot, Sha(bytes), Array.AsReadOnly(loaded.ToArray()));
    }
    public static IReadOnlyList<Truth> ParseLabels(byte[] bytes, int width, int height)
    {
        ImageGeometry.ValidateDimensions(width, height);
        Require(bytes.Length <= 131_072, "Label file too large.");
        string text = new UTF8Encoding(false, true).GetString(bytes);
        if (text.StartsWith('\uFEFF')) text = text[1..];
        List<Truth> result = [];
        foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            Require(fields.Length == 5 && int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out _), "Need integer class and exactly4 normalized coordinates.");
            int classId = int.Parse(fields[0], CultureInfo.InvariantCulture);
            Require(classId is >= 0 and < 80, "Class must be a COCO80 model index0..79, not COCO category_id.");
            double[] v = fields.Skip(1).Select(s => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
            Require(v.All(double.IsFinite) && v.All(n => n is >= 0 and <= 1) && v[2] > 0 && v[3] > 0, "Coordinates must be finite normalized values and positive sizes.");
            Box box = new((v[0] - v[2] / 2) * width, (v[1] - v[3] / 2) * height,
                (v[0] + v[2] / 2) * width, (v[1] + v[3] / 2) * height);
            BoxMath.Validate(box, width, height); // No silent clamp of a bad annotation.
            result.Add(new(classId, box)); Require(result.Count <= 128, "At most128 truth boxes per image.");
        }
        return Array.AsReadOnly(result.ToArray());
    }
    public static string Resolve(string root, string relative)
    {
        Require(!string.IsNullOrWhiteSpace(relative) && !relative.Contains('\\') && !relative.Contains(':') &&
            relative.Length <= 512 && !Path.IsPathRooted(relative) && relative.Split('/').All(s => s.Length > 0 &&
                s is not "." and not ".." && s == s.TrimEnd('.', ' ') && !s.Any(c => Path.GetInvalidFileNameChars().Contains(c))),
            "Use a relative forward-slash path inside the manifest folder.");
        string fullRoot = Path.GetFullPath(root), current = fullRoot;
        Require(!fullRoot.StartsWith(@"\\", StringComparison.Ordinal), "Trusted local folder required.");
        Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "Linked root not supported.");
        foreach (string segment in relative.Split('/'))
        {
            current = Path.Combine(current, segment);
            Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "Linked image/label paths not supported.");
        }
        return current;
    }
    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && value == value.Trim() && !value.Any(char.IsControl);
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    internal static void Require(bool ok, string message) { if (!ok) throw new ArgumentException(message); }
}
