namespace IndustrialVision;

public enum AcquisitionMode { Unknown, Continuous, SoftwareSingle, HardwareSingle }
public sealed record CaptureContext(double SampledAt, Guid CameraEpoch, Guid SourceSession, uint ProfileRevision,
    AcquisitionMode Mode = AcquisitionMode.Unknown, bool Valid = false, bool Grabbing = false,
    bool TriggerReady = false, bool ProcessPermit = false);
public readonly record struct CaptureKey(Guid OwnerSession, uint Number);
public sealed record CaptureRequest(CaptureKey Key, string Workpiece, uint ProfileRevision);
public enum CaptureStage { Prepared, TriggerTaken, AwaitingFrame, FrameMatched, NotSubmitted, Unknown }
public enum TriggerDispatchEvidence { NotAttempted, Acknowledged, Unknown }
// The adapter MUST independently establish this mapping; this class does not invent camera support.
public sealed record TriggerAssociation(CaptureKey Key, Guid CameraEpoch, ulong TriggerSequence, bool Qualified = false);
public sealed record CaptureFrame(OwnedFrame Frame, Guid CameraEpoch, uint ProfileRevision,
    ulong? TriggerSequence, double ReceivedAt, bool Complete = false, bool IdentityQualified = false);
public enum CaptureFrameDecision { Matched, Duplicate, Unrelated, Uncorrelated, Rejected, LateEvidenceOnly, Conflict }
public sealed record CaptureResult(CaptureRequest Request, CaptureFrame Evidence);
public sealed record CaptureView(CaptureRequest Request, CaptureStage Stage,
    bool DispatchAcknowledged = false, TriggerAssociation? Association = null,
    CaptureResult? Result = null, bool ProcessingTaken = false, FrameStamp? LateFrame = null);
public sealed record CaptureSessionView(bool ReconciliationRequired, string Reason,
    CaptureView? Current, CaptureResult? LastReleased);

// Serialized application observation only: one software-triggered frame per request.
// No SDK, trigger IO, exposure, buffer drain, hardware-trigger implementation or auto-recovery.
public sealed class SingleCaptureSession
{
    private readonly Guid cameraEpoch, sourceSession;
    private readonly uint profileRevision;
    private readonly int width, height;
    private readonly double maxAge, frameBudget;
    private CaptureContext? context;
    private CaptureView? current;
    private CaptureResult? lastReleased;
    private ulong? highTriggerSequence;
    private ulong highFrameNumber;
    private double lastNow;
    private double? takenAt;
    private uint number;
    private bool locked;
    private string reason = "No verified capture context";
    public Guid OwnerSession { get; } = Guid.NewGuid();

    public SingleCaptureSession(Guid cameraEpoch, Guid sourceSession, uint profileRevision, int width, int height,
        bool startupReviewed = false, double maxAge = .25, double frameBudget = 1)
    {
        if (cameraEpoch == Guid.Empty || sourceSession == Guid.Empty || profileRevision == 0
            || !double.IsFinite(maxAge) || maxAge <= 0 || !double.IsFinite(frameBudget) || frameBudget <= 0)
            throw new ArgumentException("Reviewed identities/profile and positive finite budgets required.");
        _ = Mono8Image.PixelCount(width, height); this.width = width; this.height = height;
        this.cameraEpoch = cameraEpoch; this.sourceSession = sourceSession; this.profileRevision = profileRevision;
        this.maxAge = maxAge; this.frameBudget = frameBudget;
        if (!startupReviewed) Lock("Startup/buffer/old-trigger review not declared");
    }
    private void Lock(string cause)
    {
        locked = true; reason = cause;
        if (current?.Stage == CaptureStage.Prepared) current = current with { Stage = CaptureStage.NotSubmitted };
        else if (current is not null && current.Stage is not (CaptureStage.FrameMatched or CaptureStage.NotSubmitted))
            current = current with { Stage = CaptureStage.Unknown };
    }
    private bool CheckTime(double now)
    {
        if (!double.IsFinite(now) || now < lastNow) { Lock("Invalid/backwards monotonic clock"); return false; }
        lastNow = now;
        if (context is not null && now - context.SampledAt > maxAge) Lock("Capture context coverage expired");
        if (takenAt.HasValue && current is not null && current.Stage is not (CaptureStage.FrameMatched or CaptureStage.NotSubmitted)
            && now - takenAt.Value > frameBudget) Lock("Local frame budget expired; no trigger retry or cancellation sent");
        return true;
    }
    private bool CanAct(double now) => CheckTime(now) && !locked && context is not null;
    public bool Update(CaptureContext sample, double now)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!CheckTime(now)) return false;
        if (!sample.Valid || !sample.Grabbing || !sample.ProcessPermit || sample.Mode != AcquisitionMode.SoftwareSingle
            || sample.CameraEpoch != cameraEpoch || sample.SourceSession != sourceSession || sample.ProfileRevision != profileRevision
            || !double.IsFinite(sample.SampledAt) || sample.SampledAt < 0 || sample.SampledAt > now
            || now - sample.SampledAt > maxAge || (context is not null && sample.SampledAt <= context.SampledAt))
        { Lock("Quality/session/profile/mode/grabbing/permit or sample time invalid"); return false; }
        context = sample;
        if (!locked) reason = "Verified ordinary context; trigger readiness checked at use";
        return !locked; // Fresh data never unlocks; readonly matching may still record late metadata.
    }
    public CaptureRequest? Prepare(string workpiece, double now)
    {
        if (!CanAct(now) || current is not null || !context!.TriggerReady) return null;
        if (string.IsNullOrWhiteSpace(workpiece) || workpiece.Length > 80) throw new ArgumentException("Bounded workpiece identity required.");
        if (number == uint.MaxValue) { Lock("Request numbers exhausted; never wrap"); return null; }
        CaptureRequest request = new(new(OwnerSession, ++number), workpiece, profileRevision);
        current = new(request, CaptureStage.Prepared); takenAt = null;
        return request;
    }
    public CaptureRequest? TakeTrigger(double now)
    {
        if (!CanAct(now) || current?.Stage != CaptureStage.Prepared || !context!.TriggerReady) return null;
        current = current with { Stage = CaptureStage.TriggerTaken }; takenAt = now;
        return current.Request; // A local one-time intent; does not execute software or hardware trigger.
    }
    public bool RecordDispatch(CaptureKey key, TriggerDispatchEvidence evidence, double now)
    {
        if (!CheckTime(now) || locked || current?.Request.Key != key || current.Stage != CaptureStage.TriggerTaken) return false;
        if (!Enum.IsDefined(evidence)) { Lock("Invalid trigger dispatch evidence"); return false; }
        if (evidence == TriggerDispatchEvidence.NotAttempted)
        { current = current with { Stage = CaptureStage.NotSubmitted }; Lock("Explicit not-attempted; review before another request"); }
        else if (evidence == TriggerDispatchEvidence.Unknown) Lock("Trigger dispatch unknown; no automatic re-trigger");
        else current = current with { Stage = CaptureStage.AwaitingFrame, DispatchAcknowledged = true };
        return true;
    }
    public bool Associate(TriggerAssociation association, double now)
    {
        ArgumentNullException.ThrowIfNull(association);
        if (!CheckTime(now) || current?.Request.Key != association.Key) return false;
        if (!takenAt.HasValue || current.Stage == CaptureStage.NotSubmitted || !association.Qualified || association.CameraEpoch != cameraEpoch)
        { Lock("No qualified original trigger association"); return false; }
        if (current.Association is { } previous)
        {
            if (previous == association) return true;
            Lock("Original trigger association changed"); return false;
        }
        if (highTriggerSequence.HasValue && association.TriggerSequence <= highTriggerSequence.Value)
        { Lock("Normalized trigger identity reused/regressed in same camera epoch"); return false; }
        highTriggerSequence = association.TriggerSequence;
        current = current with { Association = association };
        return true;
    }
    public CaptureFrameDecision ObserveFrame(CaptureFrame packet, double now)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (!CheckTime(now)) return CaptureFrameDecision.Rejected;
        if (current is null || packet.CameraEpoch != cameraEpoch || packet.ProfileRevision != profileRevision
            || packet.Frame is null || packet.Frame.Stamp.Session != sourceSession) return CaptureFrameDecision.Unrelated;
        if (current.Association is null || !packet.TriggerSequence.HasValue) return CaptureFrameDecision.Uncorrelated;
        if (packet.TriggerSequence.Value != current.Association.TriggerSequence) return CaptureFrameDecision.Unrelated;
        if (!takenAt.HasValue || current.Stage == CaptureStage.NotSubmitted || !packet.Complete || !packet.IdentityQualified
            || packet.Frame.Image is null || packet.Frame.Image.Width != width || packet.Frame.Image.Height != height
            || packet.Frame.Stamp.Number == 0
            || !double.IsFinite(packet.ReceivedAt) || packet.ReceivedAt < takenAt.Value
            || packet.ReceivedAt > now || now - packet.ReceivedAt > maxAge)
        { Lock("Matched identity has incomplete/unqualified/stale frame evidence"); return CaptureFrameDecision.Rejected; }
        if (current.Result is { } existing)
        {
            if (SameFrame(existing.Evidence, packet)) return CaptureFrameDecision.Duplicate;
            Lock("Same trigger yielded conflicting frame identity or pixels"); return CaptureFrameDecision.Conflict;
        }
        if (packet.Frame.Stamp.Number <= highFrameNumber)
        { Lock("Local owned-frame identity reused in this source session"); return CaptureFrameDecision.Rejected; }
        if (locked)
        {
            current = current with { LateFrame = packet.Frame.Stamp };
            return CaptureFrameDecision.LateEvidenceOnly; // Metadata only; never offer these pixels to processing.
        }
        CaptureResult result = new(current.Request, packet);
        highFrameNumber = packet.Frame.Stamp.Number;
        current = current with { Stage = CaptureStage.FrameMatched, Result = result };
        return CaptureFrameDecision.Matched;
    }
    private static bool SameFrame(CaptureFrame a, CaptureFrame b) => a.Frame.Stamp == b.Frame.Stamp
        && a.CameraEpoch == b.CameraEpoch && a.ProfileRevision == b.ProfileRevision && a.TriggerSequence == b.TriggerSequence
        && a.ReceivedAt == b.ReceivedAt && a.Frame.Image.Width == b.Frame.Image.Width && a.Frame.Image.Height == b.Frame.Image.Height
        && a.Frame.Image.CopyPixels().AsSpan().SequenceEqual(b.Frame.Image.CopyPixels());
    public CaptureResult? TakeForProcessing(double now)
    {
        if (!CanAct(now) || current?.Stage != CaptureStage.FrameMatched || current.Result is null || current.ProcessingTaken) return null;
        if (now - current.Result.Evidence.ReceivedAt > maxAge)
        { Lock("Matched image aged before processing take; history is not current work permission"); return null; }
        current = current with { ProcessingTaken = true }; return current.Result;
    }
    public bool ReleaseCapture(CaptureKey key, double now)
    {
        if (!CanAct(now) || current?.Request.Key != key || current.Result is null || !current.ProcessingTaken) return false;
        lastReleased = current.Result; current = null; takenAt = null;
        return true; // Application acquired ownership; NOT algorithm/inspection completion.
    }
    public CaptureSessionView Read(double now)
    {
        _ = CheckTime(now); return new(locked, reason, current, lastReleased);
    }
    public void Invalidate() => Lock("External close/invalidation; no camera stop, flush or auto-recovery");
}
