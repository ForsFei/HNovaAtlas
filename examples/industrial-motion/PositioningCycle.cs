namespace IndustrialMotion;

internal enum CycleStage { Idle, RetractZ, PositionXY, ApproachZ, Indicator, WaitAck, Completed, Halted }
internal enum CycleAction { RetractZ, MoveX, MoveY, ApproachZ, IndicatorOn }
internal enum CycleResult { Completed, Rejected, Stopped, Unknown }
internal readonly record struct CycleTicket(Guid Cycle, CycleAction Action);
// Normalized application inputs, NOT SDK structs or an atomic process image.
internal sealed record CycleFrame(double SampledAt, ulong Generation = 1, bool DataValid = true,
    bool InterlocksOk = true, bool? Acknowledgement = false);

// Pure serialized teaching reducer: emits INTENTS, never executes, retries, stops or resets hardware.
// Reports must already be correlated and qualified by a single-owner adapter/observer.
internal sealed class PositioningCycle
{
    private readonly double _maxAge, _maxGap, _stepTimeout;
    private readonly HashSet<CycleAction> _completed = [];
    private readonly Guid _id = Guid.NewGuid();
    private ulong _generation;
    private double _lastNow, _lastSample, _stepStarted;
    private bool _sawAckLow;
    public CycleStage Stage { get; private set; }
    public string Reason { get; private set; } = "NotStarted";
    public bool RequiresStopReview => Stage == CycleStage.Halted;
    public bool IndicatorRequested { get; private set; }
    public IReadOnlyList<CycleTicket> Pending => Array.AsReadOnly(Actions()
        .Where(a => !_completed.Contains(a)).Select(a => new CycleTicket(_id, a)).ToArray());

    public PositioningCycle(double maxAge = .25, double maxGap = .25, double stepTimeout = 3)
    {
        if (new[] { maxAge, maxGap, stepTimeout }.Any(v => !double.IsFinite(v) || v <= 0) || stepTimeout <= maxGap)
            throw new ArgumentOutOfRangeException(nameof(stepTimeout));
        _maxAge = maxAge; _maxGap = maxGap; _stepTimeout = stepTimeout;
    }
    private CycleAction[] Actions() => Stage switch
    {
        CycleStage.RetractZ => [CycleAction.RetractZ],
        CycleStage.PositionXY => [CycleAction.MoveX, CycleAction.MoveY],
        CycleStage.ApproachZ => [CycleAction.ApproachZ],
        CycleStage.Indicator => [CycleAction.IndicatorOn],
        _ => []
    };
    private void Halt(string reason) { Stage = CycleStage.Halted; Reason = reason; }
    private void Enter(CycleStage stage, double now)
    {
        Stage = stage; _stepStarted = now; _completed.Clear(); _sawAckLow = false;
        Reason = stage.ToString();
        if (stage == CycleStage.Indicator) IndicatorRequested = true;
    }
    private bool Accept(CycleFrame frame, double now, bool starting = false)
    {
        if (!double.IsFinite(now) || now < 0 || (!starting && now < _lastNow))
        { Halt("InvalidMonotonicTime"); return false; }
        _lastNow = now;
        if (!frame.DataValid || !double.IsFinite(frame.SampledAt) || frame.SampledAt < 0 ||
            frame.SampledAt > now || now - frame.SampledAt > _maxAge ||
            (!starting && (frame.SampledAt <= _lastSample || frame.SampledAt - _lastSample > _maxGap)))
        { Halt("InvalidStaleOrDiscontinuousSample"); return false; }
        if (frame.Generation == 0 || (!starting && frame.Generation != _generation))
        { Halt("GenerationChanged"); return false; }
        if (!frame.InterlocksOk) { Halt("InterlockLost"); return false; }
        _lastSample = frame.SampledAt;
        if (!starting && now - _stepStarted >= _stepTimeout) { Halt("StepTimeoutNotAStop"); return false; }
        return true;
    }
    public void Start(CycleFrame frame, double now, bool stationaryReferencedAndPlanReviewed)
    {
        if (Stage != CycleStage.Idle) throw new InvalidOperationException("One cycle per instance; no restart/replay.");
        if (!stationaryReferencedAndPlanReviewed) { Halt("StartConditionsNotConfirmed"); return; }
        if (!Accept(frame, now, starting: true)) return;
        _generation = frame.Generation;
        Enter(CycleStage.RetractZ, now);
    }
    public void Report(CycleTicket ticket, CycleResult result, CycleFrame frame, double now)
    {
        // Late, foreign and duplicate results cannot advance or refresh this cycle.
        if (!Pending.Contains(ticket)) return;
        if (!Enum.IsDefined(result)) throw new ArgumentOutOfRangeException(nameof(result));
        if (!Accept(frame, now)) return;
        if (result != CycleResult.Completed) { Halt("Action" + result); return; }
        _completed.Add(ticket.Action);
        if (Pending.Count != 0) return; // X success does not imply Y success.
        Enter(Stage switch
        {
            CycleStage.RetractZ => CycleStage.PositionXY,
            CycleStage.PositionXY => CycleStage.ApproachZ,
            CycleStage.ApproachZ => CycleStage.Indicator,
            CycleStage.Indicator => CycleStage.WaitAck,
            _ => throw new InvalidOperationException()
        }, now);
    }
    public void Observe(CycleFrame frame, double now)
    {
        if (Stage is CycleStage.Idle or CycleStage.Completed or CycleStage.Halted) return;
        if (!Accept(frame, now)) return;
        if (Stage != CycleStage.WaitAck) return;
        if (!frame.Acknowledgement.HasValue) { _sawAckLow = false; return; }
        if (!frame.Acknowledgement.Value) _sawAckLow = true;
        else if (_sawAckLow) Enter(CycleStage.Completed, now);
    }
    public void CheckDeadline(double now)
    {
        if (Stage is CycleStage.Idle or CycleStage.Completed or CycleStage.Halted) return;
        if (!double.IsFinite(now) || now < _lastNow) { Halt("InvalidMonotonicTime"); return; }
        _lastNow = now;
        if (now - _lastSample > Math.Min(_maxAge, _maxGap)) Halt("ObservationLostNotAStop");
        else if (now - _stepStarted >= _stepTimeout) Halt("StepTimeoutNotAStop");
    }
    public void Withdraw()
    {
        if (Stage is not (CycleStage.Idle or CycleStage.Completed or CycleStage.Halted)) Halt("WithdrawnNotAStop");
    }
}
