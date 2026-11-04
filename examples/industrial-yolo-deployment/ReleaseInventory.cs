using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialYolo.Onnx;
using IndustrialYolo.Review;

namespace IndustrialYolo.Deployment;

public sealed record Artifact(string Path, long Bytes, string Sha256);
public sealed record ReleaseProfile(string SchemaVersion, string ReleaseId, string Rid, string Mode,
    string ModelPath, string ModelSha256, string SettingsPath, IReadOnlyList<Artifact> Files);
public sealed record DeploymentSettings(string SchemaVersion, string SettingsVersion, string PipelineVersion,
    string LabelSet, float ScoreThreshold, double NmsThreshold);

public static class ReleaseInventory
{
    public const string ProfileName = "release-profile.json", ModelPath = "model/yolox_nano.onnx", SettingsPath = "deployment-settings.json";
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true
    };
    public static string ShaFile(string path)
    { using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static IReadOnlyList<Artifact> Scan(string root)
    {
        root = Path.GetFullPath(root); List<Artifact> items = []; long total = 0;
        void Walk(string directory)
        {
            Require((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0, "Linked directories are not supported.");
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                Require((attributes & FileAttributes.ReparsePoint) == 0, "Linked release artifacts are not supported.");
                if ((attributes & FileAttributes.Directory) != 0) { Walk(entry); continue; }
                string relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                if (relative == ProfileName) continue;
                long length = new FileInfo(entry).Length;
                Require(length <= 536_870_912 && items.Count < 512 && (total += length) <= 1_073_741_824, "Release inventory exceeds teaching file/byte limits.");
                items.Add(new(relative, length, ShaFile(entry)));
            }
        }
        Walk(root); return Array.AsReadOnly(items.OrderBy(i => i.Path, StringComparer.Ordinal).ToArray());
    }
    public static DeploymentSettings ReadSettings(string root, string relative, string? expectedSha256 = null)
    {
        byte[] bytes = ReviewData.ReadBytes(ReviewData.Resolve(root, relative), 65_536);
        Require(expectedSha256 is null || string.Equals(ReviewData.Sha(bytes), expectedSha256, StringComparison.OrdinalIgnoreCase), "Settings bytes changed after inventory check.");
        DeploymentSettings settings = JsonSerializer.Deserialize<DeploymentSettings>(bytes, Json)
            ?? throw new ArgumentException("Null deployment settings.");
        Require(settings.SchemaVersion == "atlas-yolo-deployment-settings-v1" && Text(settings.SettingsVersion) &&
            settings.PipelineVersion == ReviewData.Pipeline && settings.LabelSet == ReviewData.LabelSet &&
            float.IsFinite(settings.ScoreThreshold) && settings.ScoreThreshold is >= 0 and <= 1 &&
            double.IsFinite(settings.NmsThreshold) && settings.NmsThreshold is >= 0 and <= 1, "Settings disagree with fixed CPU image pipeline.");
        return settings;
    }
    public static ReleaseProfile ReadProfile(string path) => ReadProfile(path, out _);
    public static ReleaseProfile ReadProfile(string path, out string sha256)
    {
        Require(Path.GetFileName(path) == ProfileName, "Use the fixed release-profile.json filename.");
        byte[] bytes = ReviewData.ReadBytes(path, 1_048_576); sha256 = ReviewData.Sha(bytes);
        ReleaseProfile profile = JsonSerializer.Deserialize<ReleaseProfile>(bytes, Json)
            ?? throw new ArgumentException("Null profile.");
        Validate(profile); return profile;
    }
    internal static void Validate(ReleaseProfile profile)
    {
        Require(profile.SchemaVersion == "atlas-yolo-release-v1" && Text(profile.ReleaseId) && profile.Rid == "win-x64" &&
            profile.Mode == "framework-dependent-folder" && profile.ModelPath == ModelPath && profile.ModelSha256 == ModelArtifact.Sha256 &&
            profile.SettingsPath == SettingsPath && profile.Files is { Count: >= 1 and <= 512 }, "Unsupported release profile.");
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (Artifact file in profile.Files!)
        {
            Require(file is not null && ValidRelative(file.Path) && file.Path != ProfileName && paths.Add(file.Path) &&
                file.Bytes is >= 0 and <= 536_870_912 && file.Sha256 is { Length: 64 } && file.Sha256.All(Uri.IsHexDigit), "Invalid artifact entry.");
            total += file!.Bytes; Require(total <= 1_073_741_824, "Inventory sum too large.");
        }
        foreach (string name in new[] { "IndustrialYolo.Deployment.exe", "IndustrialYolo.Deployment.dll",
            "IndustrialYolo.Deployment.deps.json", "IndustrialYolo.Deployment.runtimeconfig.json", SettingsPath, ModelPath })
            Require(paths.Contains(name), $"Required release artifact missing: {name}");
        foreach (string native in new[] { "onnxruntime.dll", "OpenCvSharpExtern.dll" })
            Require(profile.Files!.Count(f => Path.GetFileName(f.Path).Equals(native, StringComparison.OrdinalIgnoreCase)) == 1, $"Need exactly one {native}.");
        Require(profile.Files!.Single(f => f.Path == ModelPath).Sha256 == ModelArtifact.Sha256, "Model artifact hash must match pinned contract.");
    }
    public static void Verify(string root, ReleaseProfile profile)
    {
        Validate(profile); IReadOnlyList<Artifact> observed = Scan(root);
        Require(observed.Count == profile.Files.Count, "Missing or unlisted release files.");
        Dictionary<string, Artifact> expected = profile.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        foreach (Artifact file in observed)
            Require(expected.TryGetValue(file.Path, out Artifact? match) && match.Bytes == file.Bytes &&
                string.Equals(match.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase), $"Artifact changed: {file.Path}");
    }
    public static string Seal(string root, string releaseId)
    {
        root = Path.GetFullPath(root); string destination = Path.Combine(root, ProfileName);
        Require(!File.Exists(destination) && Text(releaseId), "Profile exists or release id invalid; use a new release directory.");
        _ = ReadSettings(root, SettingsPath);
        _ = ModelArtifact.ReadVerified(ReviewData.Resolve(root, ModelPath));
        using (JsonDocument runtime = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "IndustrialYolo.Deployment.runtimeconfig.json"))))
        {
            JsonElement options = runtime.RootElement.GetProperty("runtimeOptions");
            Require(options.TryGetProperty("framework", out JsonElement framework) && framework.GetProperty("name").GetString() == "Microsoft.NETCore.App",
                "Only framework-dependent console folder publication is sealed by this tool.");
        }
        ReleaseProfile profile = new("atlas-yolo-release-v1", releaseId, "win-x64", "framework-dependent-folder", ModelPath,
            ModelArtifact.Sha256, SettingsPath, Scan(root));
        Validate(profile);
        WriteNew(profile, destination); return destination;
    }
    internal static void WriteNew(object value, string destination)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        Require(bytes.Length <= 1_048_576, "Deployment JSON exceeds1MiB.");
        if (File.Exists(destination)) throw new IOException("Output exists; use a new path.");
        string temp = destination + $".{Guid.NewGuid():N}.tmp"; bool created = false;
        try
        {
            using (FileStream file = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { created = true; file.Write(bytes); file.Flush(true); }
            File.Move(temp, destination, overwrite: false);
        }
        finally { if (created && File.Exists(temp)) File.Delete(temp); }
    }
    internal static bool ValidRelative(string? p) => !string.IsNullOrWhiteSpace(p) && p.Length <= 512 && !p.Contains('\\') && !p.Contains(':') &&
        !Path.IsPathRooted(p) && p.Split('/').All(s => s.Length > 0 && s is not "." and not ".." && s == s.TrimEnd('.', ' '));
    internal static bool Text(string? s) => !string.IsNullOrWhiteSpace(s) && s.Length <= 64 && s == s.Trim() && !s.Any(char.IsControl);
    internal static void Require(bool ok, string error) { if (!ok) throw new ArgumentException(error); }
}
