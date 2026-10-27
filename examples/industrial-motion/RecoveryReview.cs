namespace IndustrialMotion;

[Flags]
internal enum RecoveryFact
{
    None = 0, AxesObservedStopped = 1, CoordinateReleased = 2, FaultsReviewed = 4,
    ReferenceConfirmed = 8, TriggerQuiescent = 16, ShotReconciled = 32,
    WorkpieceAccounted = 64, OutputsReviewed = 128, OwnersExclusive = 256
}
internal enum WorkpieceDisposition { Unresolved, RetainForNewPlan, RemovedOrQuarantined }
internal sealed record InterruptedRun(Guid RunId, string Workpiece, bool MotionMayHaveExecuted, bool ShotMayHaveOccurred);
// Application evidence summary only. Each fact must be established outside this model.
internal sealed record RecoveryFrame(double SampledAt, Guid ConnectionEpoch, ulong ConfigurationRevision,
    ulong ReferenceRevision, string Workpiece, RecoveryFact Facts, WorkpieceDisposition Disposition, bool Valid = true);
internal readonly record struct RecoveryReviewKey(Guid Instance, Guid Basis);
internal sealed record RecoveryView(RecoveryReviewKey? Key, RecoveryFact Missing, WorkpieceDisposition Disposition,
    bool CanOfferNewPlanReview, bool CurrentReviewAcknowledged, string Reason);
internal sealed record RecoveryAcknowledgement(RecoveryReviewKey Key, InterruptedRun Original,
    RecoveryFrame Basis, double ConfirmedAt);

// Pure serialized review ledger. An acknowledgement is NOT a plan, permit, resume or device command.
internal sealed class RecoveryReview
{
    public const RecoveryFact AllRequired = (RecoveryFact)511;
    private readonly Guid _instance = Guid.NewGuid();
    private readonly double _maxAge;
    private RecoveryFrame? _frame;
    private RecoveryReviewKey? _key;
    private bool _acknowledged;
    private double _lastNow;
    private double? _lastSample;
    private string _reason = "NoRecoveryEvidence";
    public InterruptedRun Original { get; }
    // Latest receipt only, retained as history after expiry/invalidation; not a full durable audit log.
    public RecoveryAcknowledgement? LastAcknowledgement { get; private set; }

    public RecoveryReview(InterruptedRun original, double maxAge = .25)
    {
        if (original.RunId == Guid.Empty || string.IsNullOrWhiteSpace(original.Workpiece) || original.Workpiece.Length > 80 ||
            !double.IsFinite(maxAge) || maxAge <= 0) throw new ArgumentException("Invalid interrupted-run identity or age policy.");
        Original = original; _maxAge = maxAge;
    }
    private void Clear(string reason) { _frame = null; _key = null; _acknowledged = false; _reason = reason; }
    private bool CheckNow(double now)
    {
        if (!double.IsFinite(now) || now < _lastNow) { Clear("InvalidMonotonicTime"); return false; }
        _lastNow = now; return true;
    }
    public void Update(RecoveryFrame frame, double now)
    {
        if (!CheckNow(now)) return;
        if (!frame.Valid || !double.IsFinite(frame.SampledAt) || frame.SampledAt < 0 || frame.SampledAt > now ||
            now - frame.SampledAt > _maxAge || (_lastSample.HasValue && frame.SampledAt <= _lastSample.Value) ||
            frame.ConnectionEpoch == Guid.Empty || frame.ConfigurationRevision == 0 || frame.ReferenceRevision == 0 ||
            frame.Workpiece != Original.Workpiece || !Enum.IsDefined(frame.Disposition) || (frame.Facts & ~AllRequired) != 0)
        { Clear("InvalidStaleOrMismatchedRecoveryEvidence"); return; }

        bool gap = _frame is not null && (now - _frame.SampledAt > _maxAge || frame.SampledAt - _frame.SampledAt > _maxAge);
        bool sameBasis = _frame is not null && (_frame with { SampledAt = frame.SampledAt }) == frame;
        if (!sameBasis || gap)
        {
            _key = new(_instance, Guid.NewGuid()); _acknowledged = false;
        }
        _frame = frame; _lastSample = frame.SampledAt;
        _reason = _acknowledged ? "HumanReviewRecordedNotMotionPermission" : "ReviewFactsAndDisposition";
    }
    public RecoveryView Read(double now)
    {
        if (CheckNow(now) && _frame is not null && now - _frame.SampledAt > _maxAge) Clear("RecoveryEvidenceExpired");
        if (_frame is null) return new(null, AllRequired, WorkpieceDisposition.Unresolved, false, false, _reason);
        RecoveryFact missing = AllRequired & ~_frame.Facts;
        bool ready = missing == RecoveryFact.None && _frame.Disposition != WorkpieceDisposition.Unresolved && !_acknowledged;
        return new(_key, missing, _frame.Disposition, ready, _acknowledged, _reason);
    }
    public bool AcknowledgeForNewPlanReview(RecoveryReviewKey key, double now)
    {
        RecoveryView current = Read(now);
        if (!current.CanOfferNewPlanReview || current.Key != key || _frame is null) return false;
        _acknowledged = true; _reason = "HumanReviewRecordedNotMotionPermission";
        LastAcknowledgement = new(key, Original, _frame, now);
        return true;
    }
    public void Invalidate() => Clear("ExternallyInvalidatedNoAutomaticResume");
}
