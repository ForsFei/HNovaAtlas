namespace IndustrialMotion;

// Normalized teaching inputs, NOT native SDK structs or an atomic EtherCAT snapshot.
internal enum ObservedMotionKind { Home, Move, Stop }
internal enum DispatchEvidence { NotAttempted, Acknowledged, OutcomeUnknown }
internal enum AttemptState { AwaitingDispatch, WaitingForStart, Running, Settling, Completed, Stopped, Failed, Unknown, NotSubmitted }
internal enum AttemptEvidence { None, HomeResultTransition, CommandPositionOnly, FeedbackAndInPosition, ObservedStandstill }
internal sealed record ObservedAxis(double SampledAt, ulong ConnectionGeneration = 1, bool DataValid = true,
    ushort BusError = 0, ushort DriveState = 4, ushort AxisError = 0, bool LimitActive = false,
    bool IsStopped = true, ushort HomeResult = 0, int StopReason = 0, double CommandPosition = 0,
    double? FeedbackPosition = null, bool? InPosition = null);
internal sealed record ObservationPolicy(double MaxAge = .25, double MaxGap = .25, double StartTimeout = .5,
    double TotalTimeout = 3, double StableFor = .1, double Tolerance = .01)
{
    public void Validate()
    {
        double[] values = [MaxAge, MaxGap, StartTimeout, TotalTimeout, StableFor, Tolerance];
        if (values.Any(v => !double.IsFinite(v) || v <= 0) || TotalTimeout <= StartTimeout || StableFor >= TotalTimeout)
            throw new ArgumentOutOfRangeException(nameof(ObservationPolicy));
    }
}

// Pure, serialized observer. No transport, SDK call, retry, clear, enable, move or stop action.
// Requires ONE application command owner; its local Key is not a device transaction ID.
internal sealed class MotionAttempt
{
    private readonly ObservedMotionKind _kind;
    private readonly ObservationPolicy _policy;
    private readonly double? _target;
    private readonly bool _requireFeedback;
    private double _lastSample, _lastNow, _dispatchedAt;
    private double? _settledSince;
    private bool _sawMotion, _sawHomeIncomplete;
    public CommandKey Key { get; }
    public AttemptState State { get; private set; } = AttemptState.AwaitingDispatch;
    public AttemptEvidence Evidence { get; private set; }
    public string Reason { get; private set; } = "PreparedOnly";
    public bool Terminal => State is AttemptState.Completed or AttemptState.Stopped or AttemptState.Failed or AttemptState.Unknown or AttemptState.NotSubmitted;
    public bool ReferenceEstablished => _kind == ObservedMotionKind.Home && State == AttemptState.Completed;

    public MotionAttempt(ObservedMotionKind kind, CommandKey key, ObservedAxis baseline, double now,
        double? target = null, bool requireFeedback = false, ObservationPolicy? policy = null)
    {
        _policy = policy ?? new(); _policy.Validate();
        if (!Enum.IsDefined(kind) || key.ConnectionGeneration == 0 || key.Sequence == 0 ||
            !double.IsFinite(now) || now < 0 || !double.IsFinite(baseline.SampledAt) || baseline.SampledAt < 0 ||
            baseline.SampledAt > now || now - baseline.SampledAt > _policy.MaxAge || !baseline.DataValid ||
            baseline.ConnectionGeneration != key.ConnectionGeneration || baseline.BusError != 0 ||
            baseline.HomeResult > 1 || !double.IsFinite(baseline.CommandPosition) ||
            (kind == ObservedMotionKind.Move && (!target.HasValue || !double.IsFinite(target.Value))) ||
            (kind != ObservedMotionKind.Move && (target.HasValue || requireFeedback)))
            throw new ArgumentException("Invalid request/baseline. Construct BEFORE attempting dispatch.");
        _kind = kind; Key = key; _target = target; _requireFeedback = requireFeedback;
        _lastSample = baseline.SampledAt; _lastNow = now;
    }
    private void End(AttemptState state, string reason, AttemptEvidence evidence = AttemptEvidence.None)
    {
        State = state; Reason = reason; Evidence = evidence;
    }
    private bool CheckNow(double now)
    {
        if (!double.IsFinite(now) || now < _lastNow) { End(AttemptState.Unknown, "InvalidMonotonicTime"); return false; }
        _lastNow = now; return true;
    }
    public void RecordDispatch(DispatchEvidence evidence, double now)
    {
        if (State != AttemptState.AwaitingDispatch) throw new InvalidOperationException("Dispatch can be recorded once.");
        if (!Enum.IsDefined(evidence)) throw new ArgumentOutOfRangeException(nameof(evidence));
        if (!CheckNow(now)) return;
        _dispatchedAt = now;
        if (evidence == DispatchEvidence.NotAttempted) { End(AttemptState.NotSubmitted, "NoSubmissionAttempt"); return; }
        if (evidence == DispatchEvidence.OutcomeUnknown) { End(AttemptState.Unknown, "DispatchOutcomeUnknown"); return; }
        if (now - _lastSample > _policy.MaxAge) { End(AttemptState.Unknown, "BaselineExpiredDuringDispatch"); return; }
        State = AttemptState.WaitingForStart; Reason = "CallAcknowledgedNotMotionCompletion";
    }
    private bool Deadline(double now)
    {
        if (now - _dispatchedAt >= _policy.TotalTimeout) { End(AttemptState.Unknown, "ObservationTimeoutNotAStop"); return false; }
        if (_kind != ObservedMotionKind.Stop && !_sawMotion && now - _dispatchedAt >= _policy.StartTimeout)
        { End(AttemptState.Unknown, "StartNotObservedMayHaveBeenMissed"); return false; }
        return true;
    }
    public void CheckDeadline(double now)
    {
        if (Terminal) return;
        if (State == AttemptState.AwaitingDispatch) throw new InvalidOperationException("Record dispatch first.");
        if (!CheckNow(now)) return;
        if (now - _lastSample > Math.Min(_policy.MaxAge, _policy.MaxGap))
        { End(AttemptState.Unknown, "ObservationLostNotAStop"); return; }
        Deadline(now);
    }
    public void Withdraw()
    {
        if (Terminal) return;
        End(State == AttemptState.AwaitingDispatch ? AttemptState.NotSubmitted : AttemptState.Unknown,
            "ObservationWithdrawnNoStopWasSent");
    }
    public void Observe(ObservedAxis sample, double now)
    {
        if (Terminal) return; // Historical command outcome is not live telemetry or a safety interlock.
        if (State == AttemptState.AwaitingDispatch) throw new InvalidOperationException("Record dispatch first.");
        if (!CheckNow(now)) return;
        if (!sample.DataValid || sample.ConnectionGeneration != Key.ConnectionGeneration || sample.BusError != 0)
        { End(AttemptState.Unknown, "InvalidDataOrConnectionChanged"); return; }
        if (!double.IsFinite(sample.SampledAt) || sample.SampledAt <= _lastSample || sample.SampledAt < _dispatchedAt ||
            sample.SampledAt > now || now - sample.SampledAt > _policy.MaxAge || sample.SampledAt - _lastSample > _policy.MaxGap)
        { End(AttemptState.Unknown, "StaleOutOfOrderOrGappedSample"); return; }
        if (sample.HomeResult > 1 || sample.DriveState > 7 || !double.IsFinite(sample.CommandPosition) ||
            (sample.FeedbackPosition.HasValue && !double.IsFinite(sample.FeedbackPosition.Value)))
        { End(AttemptState.Unknown, "MalformedNormalizedValue"); return; }
        _lastSample = sample.SampledAt;
        if (!Deadline(now)) return;
        if (sample.AxisError != 0 || sample.LimitActive || sample.DriveState >= 6 ||
            (_kind != ObservedMotionKind.Stop && sample.DriveState != 4))
        { End(AttemptState.Failed, "AxisFaultLimitOrEnableLost"); return; }
        _sawMotion |= !sample.IsStopped;
        _sawHomeIncomplete |= sample.HomeResult == 0;
        if (!sample.IsStopped) { _settledSince = null; State = AttemptState.Running; Reason = "RunningObserved"; return; }
        if (_kind != ObservedMotionKind.Stop && !_sawMotion)
        { State = AttemptState.WaitingForStart; Reason = "StoppedIsNotNewCommandCompletion"; return; }
        bool allowedStopReason = _kind == ObservedMotionKind.Stop ? sample.StopReason is 0 or 13 or 14 : sample.StopReason == 0;
        if (!allowedStopReason) { End(AttemptState.Failed, $"AbnormalStopReason={sample.StopReason}"); return; }
        bool candidate = _kind switch
        {
            ObservedMotionKind.Home => _sawHomeIncomplete && sample.HomeResult == 1,
            ObservedMotionKind.Move => Math.Abs(sample.CommandPosition - _target!.Value) <= _policy.Tolerance &&
                (!_requireFeedback || (sample.InPosition == true && sample.FeedbackPosition.HasValue &&
                Math.Abs(sample.FeedbackPosition.Value - _target.Value) <= _policy.Tolerance)),
            _ => true
        };
        State = AttemptState.Settling;
        if (!candidate) { _settledSince = null; Reason = "StoppedButCompletionEvidenceMissing"; return; }
        _settledSince ??= sample.SampledAt;
        Reason = "CandidateNeedsRepeatedObservations";
        if (sample.SampledAt - _settledSince.Value + 1e-12 < _policy.StableFor) return;
        if (_kind == ObservedMotionKind.Stop)
            End(AttemptState.Stopped, "SDKStandstillObservedNotOriginalTargetDone", AttemptEvidence.ObservedStandstill);
        else End(AttemptState.Completed, "CompletionObservedUnderSingleOwnerContract", _kind == ObservedMotionKind.Home
            ? AttemptEvidence.HomeResultTransition : _requireFeedback ? AttemptEvidence.FeedbackAndInPosition : AttemptEvidence.CommandPositionOnly);
    }
}
