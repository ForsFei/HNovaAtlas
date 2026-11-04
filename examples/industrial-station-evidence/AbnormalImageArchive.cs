using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialVision;
namespace IndustrialStationEvidence;

public sealed record AbnormalImageDocument(int Schema, string Scope, AbnormalEvidence Diagnostic, int Width, int Height, string PixelSha256, byte[] Pixels);
// Single bounded JSON document: packed Mono8 pixels encoded as base64 by System.Text.Json.
// Separate from the old diagnostic-only store; old reconciliation never silently upgrades to image evidence.
public static class AbnormalImageArchive
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, AllowDuplicateProperties = false,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
    public static string Save(string directory, AbnormalEvidence diagnostic, OwnedFrame frame)
    {
        AbnormalEvidence.Validate(diagnostic);
        if (diagnostic.DiagnosticFrame != frame.Stamp || diagnostic.DiagnosticFrameId is null) throw new InvalidDataException("Original diagnostic frame required");
        byte[] pixels = frame.Image.CopyPixels();
        AbnormalImageDocument doc = new(1, "AbnormalPixels-DiagnosticOnly", diagnostic, frame.Image.Width, frame.Image.Height,
            Convert.ToHexString(SHA256.HashData(pixels)), pixels);
        Validate(doc); Directory.CreateDirectory(directory);
        string final = Path.Combine(directory, diagnostic.Terminal.Session.ToString("N") + "-" + diagnostic.Terminal.Cycle.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + ".json");
        if (File.Exists(final))
        {
            AbnormalImageDocument old = Read(final);
            if (old.Diagnostic != diagnostic || old.Width != doc.Width || old.Height != doc.Height || !old.Pixels.SequenceEqual(pixels)) throw new InvalidDataException("Conflicting image evidence");
            return final;
        }
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(doc, Json);
        using (FileStream f = new(final + ".pending", FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(data); f.Flush(true); }
        File.Move(final + ".pending", final, overwrite: false);
        _ = Read(final); return final;
    }
    public static AbnormalImageDocument Read(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked archive refused");
        using FileStream f = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (f.Length is <= 0 or > 1500000) throw new InvalidDataException("Archive size invalid");
        byte[] data = new byte[(int)f.Length]; f.ReadExactly(data); if (f.ReadByte() != -1) throw new InvalidDataException("Growing archive");
        AbnormalImageDocument doc = JsonSerializer.Deserialize<AbnormalImageDocument>(data, Json) ?? throw new InvalidDataException("Null archive");
        Validate(doc); return doc;
    }
    public static Mono8Image Restore(AbnormalImageDocument document) { Validate(document); return Mono8Image.FromPacked(document.Width, document.Height, document.Pixels); }
    private static void Validate(AbnormalImageDocument d)
    {
        if (d.Schema != 1 || d.Scope != "AbnormalPixels-DiagnosticOnly" || d.Diagnostic is null || d.Pixels is null ||
            d.Pixels.Length != Mono8Image.PixelCount(d.Width, d.Height) || Convert.ToHexString(SHA256.HashData(d.Pixels)) != d.PixelSha256)
            throw new InvalidDataException("Archive shape/hash invalid");
        AbnormalEvidence.Validate(d.Diagnostic);
        if (d.Diagnostic.DiagnosticFrame is null || d.Diagnostic.DiagnosticFrameId is null) throw new InvalidDataException("No qualified diagnostic frame");
        // Failed/no-inspection cycles have no valid measurement to recompute; preserve that absence.
        if (d.Diagnostic.DiagnosticMeasurement is { } m)
        {
            var recipe = IndustrialStationVision.VisionStationPort.Recipes.SingleOrDefault(r => r.Version == d.Diagnostic.Terminal.Order.RecipeVersion)
                ?? throw new InvalidDataException("Unknown teaching recipe");
            if (Mono8Image.FromPacked(d.Width,d.Height,d.Pixels).Measure(recipe.Region,recipe.Threshold) != m) throw new InvalidDataException("Measurement/pixels mismatch");
        }
    }
}
