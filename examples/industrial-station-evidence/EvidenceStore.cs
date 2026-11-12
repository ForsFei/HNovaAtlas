using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialStation;
using IndustrialStationVision;
using IndustrialVision;

namespace IndustrialStationEvidence;

public sealed record EvidenceManifest(int Schema, string Encoding, int Width, int Height, string ImageSha256,
    StationTerminal Terminal, Guid FrameId, FrameStamp Frame, InspectionResult Inspection, VisionRecipe Recipe);
public sealed record EvidenceReceipt(string DirectoryPath, EvidenceManifest Manifest);

// Trusted dedicated local directory only. Single cooperating writer, no ACL/tamper/power-loss guarantee.
public sealed class EvidenceStore : IDisposable
{
    private readonly object gate = new();
    private readonly string root;
    private readonly FileStream writer;
    private bool disposed, failed;
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    public EvidenceStore(string directory)
    {
        root = Path.GetFullPath(directory); Directory.CreateDirectory(root);
        writer = new(Path.Combine(root, ".writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            RejectLink(root);
            if (Directory.EnumerateDirectories(root, "*.pending").Any()) throw new InvalidDataException("Unfinished evidence publication; review required");
        }
        catch { writer.Dispose(); throw; }
    }
    private static string Name(Guid session, long cycle)
    {
        if (session == Guid.Empty || cycle <= 0) throw new ArgumentException("Invalid cycle");
        return session.ToString("N") + "-" + cycle.ToString("D10", CultureInfo.InvariantCulture);
    }
    public EvidenceReceipt Save(VisionEvidence evidence)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (failed) throw new InvalidOperationException("Previous publication failed; retain data and review directory");
            byte[] pixels = evidence.Frame.Image.CopyPixels();
            byte[] header = Header(evidence.Frame.Image.Width, evidence.Frame.Image.Height);
            byte[] pgm = new byte[header.Length + pixels.Length]; header.CopyTo(pgm, 0); pixels.CopyTo(pgm, header.Length);
            EvidenceManifest manifest = new(1, "Mono8-P5-255", evidence.Frame.Image.Width, evidence.Frame.Image.Height,
                Convert.ToHexString(SHA256.HashData(pgm)), evidence.Terminal, evidence.FrameId, evidence.Frame.Stamp, evidence.Inspection, evidence.Recipe);
            Validate(manifest, evidence.Frame.Image);
            string final = Path.Combine(root, Name(manifest.Terminal.Session, manifest.Terminal.Cycle));
            if (Directory.Exists(final))
            {
                EvidenceReceipt existing = Read(manifest.Terminal.Session, manifest.Terminal.Cycle);
                if (existing.Manifest != manifest) throw new InvalidDataException("Same cycle evidence conflict");
                return existing;
            }
            if (Directory.EnumerateDirectories(root, "*.pending").Any()) throw new InvalidDataException("Unfinished publication blocks new save");
            string pending = final + ".pending";
            try
            {
                Directory.CreateDirectory(pending);
                WriteNew(Path.Combine(pending, "image.pgm"), pgm);
                WriteNew(Path.Combine(pending, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, Json));
                // Same parent, no overwrite; no claim that directory metadata survives a sudden power failure.
                Directory.Move(pending, final);
                return Read(manifest.Terminal.Session, manifest.Terminal.Cycle);
            }
            catch { failed = true; throw; } // Keep pending/final evidence; never erase or retry camera work.
        }
    }
    public EvidenceReceipt Read(Guid session, long cycle)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return ReadBundle(root, session, cycle);
        }
    }
    internal static EvidenceReceipt ReadBundle(string root, Guid session, long cycle) =>
        ReadOfflineBundle(root, session, cycle, out _);

    public static EvidenceReceipt ReadOfflineBundle(string root, Guid session, long cycle, out byte[] pgm)
    {
            string folder = Path.Combine(root, Name(session, cycle)); RejectLink(folder);
        if (Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).Order().SequenceEqual(new[] { "image.pgm", "manifest.json" }) != true)
            throw new InvalidDataException("Expected exactly image.pgm and manifest.json");
        byte[] json = ReadBounded(Path.Combine(folder, "manifest.json"), 65536);
        using (JsonDocument document = JsonDocument.Parse(json)) CheckDuplicates(document.RootElement);
        EvidenceManifest m = JsonSerializer.Deserialize<EvidenceManifest>(json, Json) ?? throw new InvalidDataException("Missing manifest");
        if (m.Terminal is null || m.Terminal.Session != session || m.Terminal.Cycle != cycle) throw new InvalidDataException("Directory identity mismatch");
        int count = Mono8Image.PixelCount(m.Width, m.Height); byte[] header = Header(m.Width, m.Height);
        pgm = ReadBounded(Path.Combine(folder, "image.pgm"), Mono8Image.MaxPixels + 128);
        if (pgm.Length != header.Length + count || !pgm.AsSpan(0, header.Length).SequenceEqual(header) ||
            Convert.ToHexString(SHA256.HashData(pgm)) != m.ImageSha256) throw new InvalidDataException("Image header/length/hash mismatch");
        Validate(m, Mono8Image.FromPacked(m.Width, m.Height, pgm.AsSpan(header.Length)));
        return new(folder, m);
    }
    private static void Validate(EvidenceManifest m, Mono8Image image)
    {
        if (m.Schema != 1 || m.Encoding != "Mono8-P5-255" || m.Terminal is not { Outcome: CycleOutcome.Completed } t ||
            string.IsNullOrWhiteSpace(t.Reason) || t.Reason.Length > 512 || t.Order is null || t.Session == Guid.Empty || t.Cycle <= 0 || t.Order.Producer == Guid.Empty || t.Order.Number <= 0 ||
            string.IsNullOrWhiteSpace(t.Order.Workpiece) || t.Order.Workpiece.Length > 64 || t.Order.Workpiece != t.Order.Workpiece.Trim() || t.Order.Workpiece.Any(char.IsControl) ||
            m.FrameId == Guid.Empty || t.FrameId != m.FrameId || t.Judgment is null || !Enum.IsDefined(t.Judgment.Value) ||
            m.Frame.Session == Guid.Empty || m.Frame.Number == 0 || m.Inspection is not { Stage: ProcessingStage.Completed, Measurement: not null } r ||
            r.Frame != m.Frame || r.PipelineSession == Guid.Empty || r.CameraEpoch == Guid.Empty || r.TriggerSequence == 0 ||
            r.Request is null || r.Request.Key.OwnerSession == Guid.Empty || r.Request.Key.Number == 0 || r.Request.Workpiece != t.Order.Workpiece ||
            r.ErrorCode is not null || m.Recipe is null || m.Recipe.Version != t.Order.RecipeVersion ||
            r.Recipe != m.Recipe.Processing || r.Request.ProfileRevision != r.Recipe.ProfileRevision ||
            m.Recipe != VisionStationPort.Recipes.SingleOrDefault(p => p.Version == m.Recipe.Version))
            throw new InvalidDataException("Evidence identity/recipe/result invalid");
        PixelStatistics actual = image.Measure(m.Recipe.Region, m.Recipe.Threshold);
        Verdict verdict = actual.Mean >= m.Recipe.MinimumMean && actual.AtOrAboveThreshold >= m.Recipe.MinimumBrightPixels ? Verdict.Ok : Verdict.Ng;
        if (r.Measurement != actual || t.Judgment != verdict) throw new InvalidDataException("Pixels/statistics/verdict mismatch");
    }
    private static byte[] Header(int width, int height) => Encoding.ASCII.GetBytes(FormattableString.Invariant($"P5\n{width} {height}\n255\n"));
    private static void WriteNew(string path, byte[] bytes)
    { using FileStream f = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); f.Write(bytes); f.Flush(flushToDisk: true); }
    private static byte[] ReadBounded(string path, int maximum)
    {
        RejectLink(path); using FileStream f = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (f.Length <= 0 || f.Length > maximum) throw new InvalidDataException("Evidence file size invalid");
        byte[] data = new byte[(int)f.Length]; f.ReadExactly(data); if (f.ReadByte() != -1) throw new InvalidDataException("File grew during read"); return data;
    }
    private static void RejectLink(string path)
    { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Links not accepted"); }
    private static void CheckDuplicates(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (JsonProperty p in e.EnumerateObject()) { if (!names.Add(p.Name)) throw new InvalidDataException("Duplicate JSON field"); CheckDuplicates(p.Value); }
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (JsonElement item in e.EnumerateArray()) CheckDuplicates(item);
    }
    public void Dispose() { lock (gate) { if (!disposed) { disposed = true; writer.Dispose(); } } }
}
