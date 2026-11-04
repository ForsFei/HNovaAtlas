using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialStationCommissioning;
namespace IndustrialStationHandover;

public sealed record PackageFile(string Name, long Bytes, string Sha256);
public sealed record HandoverManifest(int Schema, string Scope, string ReleaseId, IReadOnlyList<PackageFile> Files);
public sealed record PackageVerification(IReadOnlyList<string> Issues)
{
    public bool FilesMatchManifest => Issues.Count == 0; // Content agreement only, not authentication or field approval.
}
public static class HandoverPackage
{
    private const string ManifestName = "handover-manifest.json";
    public static IReadOnlyList<string> RequiredNames { get; } = Array.AsReadOnly(new[] {
        "site-parameters.template.json", "maintenance-example.json", "acceptance-report.json", "handover.md" });
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, RespectRequiredConstructorParameters = true, AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    public static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static string CreateExample(string directory, string releaseId)
    {
        if (string.IsNullOrWhiteSpace(releaseId) || releaseId.Length > 128 || releaseId != releaseId.Trim() || releaseId.Any(char.IsControl)) throw new ArgumentException("Release id required");
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Use a new handover directory");
        Directory.CreateDirectory(directory);
        SiteParameters template = SiteParameters.Template(releaseId); byte[] settings = JsonSerializer.SerializeToUtf8Bytes(template, Json);
        WriteNew(Path.Combine(directory, RequiredNames[0]), settings);
        MaintenanceRecord note = new(1, true, Guid.NewGuid(), MaintenanceKind.StartupReview, DateTimeOffset.UtcNow, "offline-demo", releaseId,
            Sha(settings), Sha(settings), "template review only; no actual machine inspection", "Example record, not a production event or recovery permission");
        note.Validate(); WriteNew(Path.Combine(directory, RequiredNames[1]), JsonSerializer.SerializeToUtf8Bytes(note, Json));
        AcceptanceBook book = new(new(releaseId, "no-native-sdk", "unfilled-template", "not-site-verified", "not-site-verified", "field-criteria-pending"));
        book.WriteNewReport(directory);
        WriteNew(Path.Combine(directory, RequiredNames[3]), Encoding.UTF8.GetBytes(
            "# Offline handover material example\n\nNo executable application, native SDK, driver, device credentials or field approval is included.\n" +
            "Fill and version a separate site configuration; keep the sealed material unchanged. Store operations and evidence outside the release directory.\n" +
            "All eight field acceptance cases remain missing. Reconcile old work before any separately approved device operation.\n"));
        HandoverManifest manifest = new(1, "TeachingMaterialsOnly-NoExecutableOrMachinePermission", releaseId, Scan(directory));
        WriteNew(Path.Combine(directory, ManifestName), JsonSerializer.SerializeToUtf8Bytes(manifest, Json));
        return Path.GetFullPath(directory);
    }
    // Read-only local verification, fixed flat filenames. Does not execute files, load settings or contact devices.
    public static PackageVerification Verify(string directory)
    {
        List<string> issues = new();
        try
        {
            RejectLink(directory); byte[] raw = ReadBounded(Path.Combine(directory, ManifestName));
            HandoverManifest m = JsonSerializer.Deserialize<HandoverManifest>(raw, Json) ?? throw new InvalidDataException("Null manifest");
            if (m.Schema != 1 || m.Scope != "TeachingMaterialsOnly-NoExecutableOrMachinePermission" || string.IsNullOrWhiteSpace(m.ReleaseId) ||
                m.ReleaseId.Length > 128 || m.Files is null || m.Files.Count != 4 ||
                m.Files.Any(f => f is null || !RequiredNames.Contains(f.Name, StringComparer.Ordinal) || f.Bytes is <= 0 or > 65536 ||
                    f.Sha256 is not { Length: 64 } || !f.Sha256.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F')) ||
                m.Files.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
                throw new InvalidDataException("Invalid manifest shape/paths");
            PackageFile[] actual = Scan(directory).ToArray();
            foreach (PackageFile expected in m.Files)
            {
                PackageFile? item = actual.SingleOrDefault(f => f.Name == expected.Name);
                if (item is null) issues.Add("Missing:" + expected.Name);
                else if (item != expected) issues.Add("Changed:" + expected.Name);
            }
            foreach (PackageFile item in actual)
                if (!RequiredNames.Contains(item.Name, StringComparer.Ordinal)) issues.Add("Unexpected:" + item.Name);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException)
        { issues.Add("UnreadableOrInvalidPackage:" + e.GetType().Name); }
        return new(issues.AsReadOnly());
    }
    private static IReadOnlyList<PackageFile> Scan(string directory)
    {
        RejectLink(directory); string[] paths = Directory.EnumerateFileSystemEntries(directory).Take(7).ToArray();
        if (paths.Length > 6) throw new InvalidDataException("Teaching package entry limit exceeded");
        List<PackageFile> files = new();
        foreach (string path in paths.Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(path) == ManifestName) continue;
            byte[] bytes = ReadBounded(path); files.Add(new(Path.GetFileName(path), bytes.LongLength, Sha(bytes)));
        }
        return files.AsReadOnly();
    }
    private static byte[] ReadBounded(string path)
    {
        RejectLink(path); using FileStream f = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (f.Length is <= 0 or > 65536) throw new InvalidDataException("File outside teaching bound");
        byte[] bytes = new byte[(int)f.Length]; f.ReadExactly(bytes); if (f.ReadByte() != -1) throw new InvalidDataException("Growing file"); return bytes;
    }
    private static void RejectLink(string path)
    { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Link rejected"); }
    private static void WriteNew(string final, byte[] bytes)
    {
        string pending = final + ".pending";
        using (FileStream f = new(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(bytes); f.Flush(true); }
        File.Move(pending, final, overwrite: false);
    }
}
