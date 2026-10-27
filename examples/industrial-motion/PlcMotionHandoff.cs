namespace IndustrialMotion;

internal readonly record struct PlcJobKey(Guid PlcBoot, ushort RequestId);
internal sealed record StationMotionJob(PlcJobKey Key, string Workpiece, uint RecipeRevision, ushort PositionId);
internal sealed record PlcHandoffInput(double SampledAt, Guid PlcBoot, bool Valid = false, bool RemoteAuto = false, bool ProcessPermit = false);
internal readonly record struct ExecutorTicket(Guid ExecutorSession, Guid Attempt);
internal enum HandoffStage { Accepted, DispatchPending, CallAcknowledged, Running, Completed, Rejected, NotSubmitted, Stopped, Failed, Unknown }
internal enum HandoffReply { Accepted, Rejected, Duplicate, Conflict, Busy, Stale, InvalidIdentity, Blocked }
internal enum MotionReportKind { Running, Completed, Stopped, Failed, Unknown }
internal sealed record MotionExecutorReport(ExecutorTicket Ticket, double SampledAt, MotionReportKind Kind, bool Qualified = false);
internal sealed record RetainedMotionJob(StationMotionJob Job, ExecutorTicket Ticket, HandoffStage Stage)
{
    public bool Terminal => Stage is HandoffStage.Completed or HandoffStage.Rejected or HandoffStage.NotSubmitted or HandoffStage.Stopped or HandoffStage.Failed or HandoffStage.Unknown;
}
internal sealed record MotionExecutionIntent(StationMotionJob Job, ExecutorTicket Ticket);
internal sealed record HandoffView(Guid ExecutorSession, ushort HighWater, bool ReconciliationRequired, string Reason, RetainedMotionJob? Retained);

// One verified PLC boot, one serialized PC owner, one retained job. Pure in-memory handoff only.
// No PLC wire codec, LTDMC, motion execution, native cancellation, stop, enable or recovery API.
internal sealed class PlcMotionHandoff
{
    private readonly Guid _plcBoot;
    private readonly uint _recipeRevision;
    private readonly HashSet<ushort> _positions;
    private readonly double _maxAge;
    private PlcHandoffInput? _input;
    private RetainedMotionJob? _current;
    private double _lastNow, _lastReport;
    private double? _lastSample;
    private ushort _highWater;
    private bool _blocked;
    private string _reason = "NoVerifiedInput";
    public Guid ExecutorSession { get; } = Guid.NewGuid();
    public int IntentsTaken { get; private set; }

    public PlcMotionHandoff(Guid verifiedPlcBoot, uint recipeRevision, IEnumerable<ushort> allowedPositions, double maxAge = .25)
    {
        ArgumentNullException.ThrowIfNull(allowedPositions);
        _positions = allowedPositions.ToHashSet();
        if (verifiedPlcBoot == Guid.Empty || recipeRevision == 0 || _positions.Count is < 1 or > 16 || _positions.Contains(0) ||
            !double.IsFinite(maxAge) || maxAge <= 0) throw new ArgumentException("Invalid verified context or fixed teaching recipe.");
        _plcBoot = verifiedPlcBoot; _recipeRevision = recipeRevision; _maxAge = maxAge;
    }
    private void Block(string reason)
    {
        _blocked = true; _reason = reason;
        if (_current?.Stage == HandoffStage.Accepted) _current = _current with { Stage = HandoffStage.NotSubmitted };
        else if (_current is not null && !_current.Terminal) _current = _current with { Stage = HandoffStage.Unknown };
    }
    private bool CheckNow(double now)
    {
        if (!double.IsFinite(now) || now < _lastNow) { Block("InvalidMonotonicTime"); return false; }
        _lastNow = now; return true;
    }
    private bool Ready(double now)
    {
        if (!CheckNow(now) || _blocked) return false;
        if (_input is null) return false;
        if (now - _input.SampledAt > _maxAge) { Block("PlcInputExpiredNoAutomaticReplay"); return false; }
        return true;
    }
    public void Update(PlcHandoffInput input, double now)
    {
        if (!CheckNow(now) || _blocked) return;
        if (!input.Valid || !double.IsFinite(input.SampledAt) || input.SampledAt < 0 || input.SampledAt > now ||
            now - input.SampledAt > _maxAge || (_lastSample.HasValue && input.SampledAt <= _lastSample.Value) ||
            (_lastSample.HasValue && input.SampledAt - _lastSample.Value > _maxAge) ||
            (_input is not null && now - _input.SampledAt > _maxAge) ||
            input.PlcBoot != _plcBoot || !input.RemoteAuto || !input.ProcessPermit)
        { Block("PlcEpochQualityModePermitOrCoverageLost"); return; }
        _input = input; _lastSample = input.SampledAt; _reason = "VerifiedInputNotMotionPermission";
    }
    public HandoffReply Receive(StationMotionJob job, double now)
    {
        if (!Ready(now)) return HandoffReply.Blocked;
        if (job.Key.PlcBoot != _plcBoot || job.Key.RequestId == 0) return HandoffReply.InvalidIdentity;
        if (_current is not null && job.Key == _current.Job.Key)
            return job == _current.Job ? HandoffReply.Duplicate : HandoffReply.Conflict;
        if (_current is not null) return HandoffReply.Busy;
        if (job.Key.RequestId <= _highWater) return HandoffReply.Stale;
        _highWater = job.Key.RequestId;
        bool valid = !string.IsNullOrWhiteSpace(job.Workpiece) && job.Workpiece.Length <= 80 &&
            job.RecipeRevision == _recipeRevision && _positions.Contains(job.PositionId);
        _current = new(job, new(ExecutorSession, Guid.NewGuid()), valid ? HandoffStage.Accepted : HandoffStage.Rejected);
        return valid ? HandoffReply.Accepted : HandoffReply.Rejected;
    }
    public MotionExecutionIntent? TakeIntent(double now)
    {
        if (!Ready(now) || _current?.Stage != HandoffStage.Accepted) return null;
        _current = _current with { Stage = HandoffStage.DispatchPending };
        _lastReport = now; IntentsTaken++;
        return new(_current.Job, _current.Ticket); // Local one-time take, NOT proof of any native call.
    }
    public bool RecordDispatch(ExecutorTicket ticket, DispatchEvidence evidence, double now)
    {
        if (_current?.Ticket != ticket || _current.Stage != HandoffStage.DispatchPending) return false;
        if (!Ready(now)) return false;
        if (!Enum.IsDefined(evidence)) { Block("InvalidDispatchEvidence"); return false; }
        _current = _current with { Stage = evidence switch
        {
            DispatchEvidence.NotAttempted => HandoffStage.NotSubmitted,
            DispatchEvidence.Acknowledged => HandoffStage.CallAcknowledged,
            _ => HandoffStage.Unknown
        } };
        if (evidence == DispatchEvidence.OutcomeUnknown) Block("DispatchUnknownRequiresReconciliation");
        return true;
    }
    public bool Observe(MotionExecutorReport report, double now)
    {
        if (_current?.Ticket != report.Ticket || _current.Stage is not (HandoffStage.CallAcknowledged or HandoffStage.Running)) return false;
        if (!Ready(now)) return false;
        if (!report.Qualified || !Enum.IsDefined(report.Kind) || !double.IsFinite(report.SampledAt) ||
            report.SampledAt > now || report.SampledAt <= _lastReport || now - report.SampledAt > _maxAge)
        { Block("UnqualifiedOrStaleExecutorReport"); return false; }
        _lastReport = report.SampledAt;
        _current = _current with { Stage = report.Kind switch
        {
            MotionReportKind.Running => HandoffStage.Running,
            MotionReportKind.Completed => HandoffStage.Completed,
            MotionReportKind.Stopped => HandoffStage.Stopped,
            MotionReportKind.Failed => HandoffStage.Failed,
            _ => HandoffStage.Unknown
        } };
        if (report.Kind is MotionReportKind.Stopped or MotionReportKind.Failed or MotionReportKind.Unknown)
            Block("ExecutorNotCompletedRequiresReconciliation");
        return true;
    }
    public bool AcknowledgeResult(PlcJobKey key, Guid expectedExecutorSession, double now)
    {
        if (!Ready(now) || _current is null || !_current.Terminal || _current.Job.Key != key || expectedExecutorSession != ExecutorSession)
            return false;
        _current = null; // High-water remains. This acknowledges a record; it does not reset hardware.
        return true;
    }
    public HandoffView Read(double now)
    {
        _ = Ready(now); // Readable retained HISTORY also survives a sticky reconciliation block.
        return new(ExecutorSession, _highWater, _blocked, _reason, _current);
    }
    public void Invalidate() => Block("ExternalInvalidationNoStopOrAutomaticRecovery");
}
