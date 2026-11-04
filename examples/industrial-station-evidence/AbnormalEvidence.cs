using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialStation;
using IndustrialStationIntegrated;
using IndustrialVision;

namespace IndustrialStationEvidence;

public enum AbnormalFrameState { NoQualifiedFrame, CaptureWithoutQualifiedFrame, UnacceptedFrame, AcceptedFrame }
public sealed record AbnormalEvidence(int Schema, string Kind, StationTerminal Terminal, AbnormalFrameState FrameState,
    OperationKey? CaptureOperation, Guid? DiagnosticFrameId, FrameStamp? DiagnosticFrame, PixelStatistics? DiagnosticMeasurement,
    Verdict? DiagnosticJudgment, string? ProcessingError)
{
    public static AbnormalEvidence Capture(ApplicationSnapshot app, IntegratedPortSnapshot devices)
    {
        StationSnapshot s = app.Host.Station;
        if (app.Busy || app.Host.TaskActive || devices.TaskActive || devices.Motion.Busy || devices.Vision.TaskActive || s.Pending is not null ||
            s.Terminal is not { Outcome: CycleOutcome.Faulted or CycleOutcome.Stopped } t || s.Session != t.Session || s.Cycle != t.Cycle || s.Order != t.Order)
            throw new InvalidOperationException("Settled local abnormal terminal required");
        var v = devices.Vision; OperationKey? key = v.CaptureOperation;
        bool sameCycle = key is not null && key.Session == t.Session && key.Cycle == t.Cycle;
        if (sameCycle && (v.Workpiece != t.Order.Workpiece || v.RecipeVersion != t.Order.RecipeVersion)) throw new InvalidDataException("Diagnostic identity conflict");
        AbnormalFrameState state = !sameCycle ? AbnormalFrameState.NoQualifiedFrame : v.FrameId is null ? AbnormalFrameState.CaptureWithoutQualifiedFrame :
            t.FrameId is null ? AbnormalFrameState.UnacceptedFrame : AbnormalFrameState.AcceptedFrame;
        AbnormalEvidence record = new(1, "DiagnosticOnly-NoPixels", t, state, sameCycle ? key : null,
            sameCycle ? v.FrameId : null, sameCycle ? v.FrameStamp : null, sameCycle ? v.Measurement : null,
            sameCycle ? v.Judgment : null, sameCycle ? v.ProcessingError : null);
        Validate(record); return record;
    }
    internal static void Validate(AbnormalEvidence r)
    {
        if (r.Schema != 1 || r.Kind != "DiagnosticOnly-NoPixels" || !Enum.IsDefined(r.FrameState) ||
            r.Terminal is not { Outcome: CycleOutcome.Faulted or CycleOutcome.Stopped } t || t.Order is null ||
            t.Session == Guid.Empty || t.Cycle <= 0 || t.Order.Producer == Guid.Empty || t.Order.Number <= 0 ||
            string.IsNullOrWhiteSpace(t.Order.Workpiece) || t.Order.Workpiece.Length > 64 || t.Order.Workpiece != t.Order.Workpiece.Trim() || t.Order.Workpiece.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(t.Order.RecipeVersion) || t.Order.RecipeVersion.Length > 64 || t.Order.RecipeVersion != t.Order.RecipeVersion.Trim() || t.Order.RecipeVersion.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(t.Reason) || t.Reason.Length > 512 || t.FrameId == Guid.Empty ||
            t.Judgment is not null && (t.FrameId is null || !Enum.IsDefined(t.Judgment.Value))) throw new InvalidDataException("Invalid abnormal terminal");
        bool hasFrame = r.FrameState is AbnormalFrameState.AcceptedFrame or AbnormalFrameState.UnacceptedFrame;
        if (hasFrame != r.DiagnosticFrameId.HasValue || hasFrame != r.DiagnosticFrame.HasValue || r.DiagnosticFrameId == Guid.Empty ||
            r.DiagnosticFrame is { } stamp && (stamp.Session == Guid.Empty || stamp.Number == 0) ||
            r.FrameState == AbnormalFrameState.AcceptedFrame && t.FrameId != r.DiagnosticFrameId ||
            r.FrameState != AbnormalFrameState.AcceptedFrame && t.FrameId is not null ||
            r.FrameState == AbnormalFrameState.NoQualifiedFrame && r.CaptureOperation is not null ||
            r.FrameState != AbnormalFrameState.NoQualifiedFrame && (r.CaptureOperation is not { Step: StepKind.Capture } op || op.Operation <= 0 || op.Session != t.Session || op.Cycle != t.Cycle) ||
            !hasFrame && (r.DiagnosticMeasurement is not null || r.DiagnosticJudgment is not null || r.ProcessingError is not null) ||
            r.DiagnosticMeasurement is { } m && (!double.IsFinite(m.Mean) || m.Count <= 0 || m.Count > Mono8Image.MaxPixels ||
                m.Region.X < 0 || m.Region.Y < 0 || m.Region.Width <= 0 || m.Region.Height <= 0 || (long)m.Region.Width * m.Region.Height != m.Count ||
                m.Min > m.Max || m.Mean < m.Min || m.Mean > m.Max || m.AtOrAboveThreshold < 0 || m.AtOrAboveThreshold > m.Count ||
                m.BlackPixels < 0 || m.WhitePixels < 0 || (long)m.BlackPixels + m.WhitePixels > m.Count) ||
            t.Judgment is not null && t.Judgment != r.DiagnosticJudgment ||
            r.DiagnosticJudgment is not null && (r.DiagnosticMeasurement is null || !Enum.IsDefined(r.DiagnosticJudgment.Value)) ||
            r.ProcessingError is not null && (r.ProcessingError.Length is 0 or > 512 || r.DiagnosticMeasurement is not null || r.DiagnosticJudgment is not null))
            throw new InvalidDataException("Invalid diagnostic classification");
    }
}

// Separate diagnostic-only records; never pretends that pixels were saved.
public sealed class AbnormalEvidenceStore : IDisposable
{
    private readonly string root;
    private readonly FileStream writer;
    private bool failed, disposed;
    private readonly object gate = new();
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, RespectRequiredConstructorParameters = true, AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    public AbnormalEvidenceStore(string directory)
    {
        root = Path.GetFullPath(directory); Directory.CreateDirectory(root); EvidenceReconciler.RejectLink(root);
        writer = new(Path.Combine(root, ".writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { if (Directory.EnumerateFileSystemEntries(root, "*.pending").Any()) throw new InvalidDataException("Pending abnormal evidence requires review"); }
        catch { writer.Dispose(); throw; }
    }
    internal static string Name(Guid session, long cycle) => session.ToString("N") + "-" + cycle.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + ".json";
    public string Save(AbnormalEvidence record)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this); if (failed) throw new InvalidOperationException("FailedStoreRequiresReview");
            AbnormalEvidence.Validate(record); string path = Path.Combine(root, Name(record.Terminal.Session, record.Terminal.Cycle));
            if (File.Exists(path)) { if (ReadRecord(path) != record) throw new InvalidDataException("Abnormal evidence conflict"); return path; }
            string pending = path + ".pending";
            try
            {
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(record, Json);
                if (bytes.Length > 65536) throw new InvalidDataException("Record too large");
                using (FileStream f = new(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(bytes); f.Flush(true); }
                File.Move(pending, path, overwrite: false);
                if (ReadRecord(path) != record) throw new InvalidDataException("Abnormal readback mismatch"); return path;
            }
            catch { failed = true; throw; }
        }
    }
    internal static AbnormalEvidence ReadRecord(string path)
    {
        EvidenceReconciler.RejectLink(path); using FileStream f = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (f.Length is <= 0 or > 65536) throw new InvalidDataException("Invalid abnormal file size");
        byte[] data = new byte[(int)f.Length]; f.ReadExactly(data); if (f.ReadByte() != -1) throw new InvalidDataException("Growing file");
        AbnormalEvidence record = JsonSerializer.Deserialize<AbnormalEvidence>(data, Json) ?? throw new InvalidDataException("Null record");
        AbnormalEvidence.Validate(record); return record;
    }
    public void Dispose() { lock (gate) { if (!disposed) { disposed = true; writer.Dispose(); } } }
}
