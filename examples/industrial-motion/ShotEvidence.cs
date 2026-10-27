namespace IndustrialMotion;

internal enum ShotState { Waiting, EvidenceCorrelated, Unknown }
// Normalized application contracts, NOT native SDK data or automatically available camera metadata.
// ArmEpoch changes after every re-arm/reset; CameraEpoch changes after acquisition/counter restart.
internal sealed record ShotBaseline(double SampledAt, Guid ArmEpoch, Guid CameraEpoch,
    int CompareCount, ulong FrameId, ulong ExpectedCameraTrigger, bool ContractsConfirmed = false);
internal sealed record CompareEvidence(double SampledAt, Guid ArmEpoch, int ProcessedCount, bool Valid = true, bool Overflow = false);
internal sealed record LatchEvidence(double SampledAt, Guid ArmEpoch, double EngineeringPosition,
    bool SingleShotAssociationConfirmed = false, bool Valid = true, bool Overflow = false);
internal sealed record FrameEvidence(double ObservedAt, Guid CameraEpoch, ulong FrameId,
    ulong? CameraTriggerOrdinal, bool Complete = true);

// One shot, serialized caller. Stores evidence only: no SDK, image buffers, IO, retry, stop or re-arm.
internal sealed class ShotEvidence
{
    private readonly ShotBaseline _baseline;
    private readonly double _target, _tolerance, _started, _timeout, _maxAge;
    private double _lastNow, _lastCompareSample, _lastLatchSample, _lastFrameObservation;
    public string Workpiece { get; }
    public ShotState State { get; private set; }
    public string Reason { get; private set; } = "AwaitingIndependentEvidence";
    public bool ComparisonObserved { get; private set; }
    public double? LatchedPosition { get; private set; }
    public ulong? MatchedFrameId { get; private set; }

    public ShotEvidence(string workpiece, ShotBaseline baseline, double target, double tolerance,
        double now, double timeout = 1, double maxAge = .25)
    {
        if (string.IsNullOrWhiteSpace(workpiece) || workpiece.Length > 80 || !baseline.ContractsConfirmed ||
            baseline.ArmEpoch == Guid.Empty || baseline.CameraEpoch == Guid.Empty ||
            baseline.CompareCount < 0 || baseline.CompareCount == int.MaxValue || baseline.FrameId == ulong.MaxValue ||
            baseline.ExpectedCameraTrigger == 0 || !double.IsFinite(target) ||
            !double.IsFinite(tolerance) || tolerance <= 0 || !double.IsFinite(now) || now < 0 ||
            !double.IsFinite(timeout) || timeout <= 0 || !double.IsFinite(maxAge) || maxAge <= 0 ||
            !double.IsFinite(baseline.SampledAt) || baseline.SampledAt < 0 ||
            baseline.SampledAt > now || now - baseline.SampledAt > maxAge)
            throw new ArgumentException("Invalid/unconfirmed one-shot contract or baseline.");
        Workpiece = workpiece; _baseline = baseline; _target = target; _tolerance = tolerance;
        _started = _lastNow = now; _timeout = timeout; _maxAge = maxAge;
        _lastCompareSample = _lastLatchSample = _lastFrameObservation = baseline.SampledAt;
    }
    private void Unknown(string reason) { State = ShotState.Unknown; Reason = reason; }
    private bool CheckNow(double now)
    {
        if (State != ShotState.Waiting) return false;
        if (!double.IsFinite(now) || now < _lastNow) { Unknown("InvalidMonotonicTime"); return false; }
        _lastNow = now;
        if (now - _started >= _timeout) { Unknown("DeadlineNotAStopOrACameraCancel"); return false; }
        return true;
    }
    private bool Sample(double sampledAt, double now, ref double previous)
    {
        if (!double.IsFinite(sampledAt) || sampledAt < 0 || sampledAt > now ||
            sampledAt <= previous || now - sampledAt > _maxAge)
        { Unknown("InvalidStaleOrRepeatedEvidence"); return false; }
        previous = sampledAt; return true;
    }
    private void FinishIfReady()
    {
        if (ComparisonObserved && LatchedPosition.HasValue && MatchedFrameId.HasValue)
        { State = ShotState.EvidenceCorrelated; Reason = "ThreeQualifiedEvidenceRecordsNotPhysicalAcceptance"; }
    }
    public void ObserveCompare(CompareEvidence value, double now)
    {
        if (!CheckNow(now) || !Sample(value.SampledAt, now, ref _lastCompareSample)) return;
        if (value.ArmEpoch != _baseline.ArmEpoch || !value.Valid || value.Overflow)
        { Unknown("ComparisonEpochQualityOrOverflow"); return; }
        long delta = (long)value.ProcessedCount - _baseline.CompareCount;
        if (delta < 0 || delta > 1) { Unknown("CounterResetOrMultipleComparisons"); return; }
        if (ComparisonObserved && delta != 1) { Unknown("ComparisonCountRegressed"); return; }
        ComparisonObserved = delta == 1;
        FinishIfReady(); // Count increment alone says nothing about a physical edge or image.
    }
    public void ObserveLatch(LatchEvidence value, double now)
    {
        if (!CheckNow(now) || !Sample(value.SampledAt, now, ref _lastLatchSample)) return;
        if (value.ArmEpoch != _baseline.ArmEpoch || !value.Valid || value.Overflow ||
            !value.SingleShotAssociationConfirmed || !double.IsFinite(value.EngineeringPosition))
        { Unknown("LatchEpochQualityOrAssociation"); return; }
        if (Math.Abs(value.EngineeringPosition - _target) > _tolerance)
        { Unknown("LatchPositionOutsideConfirmedContract"); return; }
        if (LatchedPosition.HasValue && LatchedPosition.Value != value.EngineeringPosition)
        { Unknown("ConflictingLatchRecords"); return; }
        LatchedPosition = value.EngineeringPosition;
        FinishIfReady();
    }
    public void ObserveFrame(FrameEvidence value, double now)
    {
        if (!CheckNow(now)) return;
        if (value.CameraEpoch != _baseline.CameraEpoch) { Unknown("CameraAcquisitionEpochChanged"); return; }
        if (value.FrameId <= _baseline.FrameId) return; // Known pre-arm frame. Does not renew total deadline.
        if (!Sample(value.ObservedAt, now, ref _lastFrameObservation)) return;
        if (!value.CameraTriggerOrdinal.HasValue) { Unknown("MissingCameraTriggerIdentityNoFifoGuess"); return; }
        if (value.CameraTriggerOrdinal.Value < _baseline.ExpectedCameraTrigger) return;
        if (value.CameraTriggerOrdinal.Value != _baseline.ExpectedCameraTrigger || !value.Complete)
        { Unknown("UnexpectedTriggerOrIncompleteFrame"); return; }
        if (MatchedFrameId.HasValue && MatchedFrameId.Value != value.FrameId)
        { Unknown("MultipleFramesForSingleTrigger"); return; }
        MatchedFrameId = value.FrameId;
        FinishIfReady();
    }
    public void CheckDeadline(double now) => CheckNow(now);
    public void Invalidate() { if (State == ShotState.Waiting) Unknown("EvidenceLostNoAutomaticReplay"); }
}
