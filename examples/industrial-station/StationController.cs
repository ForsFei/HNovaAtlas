namespace IndustrialStation;

public enum StationStage { Idle, AwaitingMove, AwaitingCapture, AwaitingInspection, AwaitingResultAck, Completed, Faulted, Stopped, Closing, Closed }
public enum StepKind { Move, Capture, Inspect, PublishResult }
public enum Verdict { Ok, Ng }
public enum CycleOutcome { Completed, Faulted, Stopped }
public enum Admission { Accepted, AlreadyTracked, Conflict, Stale, Busy, NotReady, Invalid, Closed }
public enum ReplyDisposition { Advanced, TerminalCreated, DrainedLate, IgnoredCorrelation, RejectedPayload, NoPending }
public sealed record WorkOrder(Guid Producer, long Number, string Workpiece, string RecipeVersion);
public sealed record StartEvidence(bool CommunicationReady, bool MotionReady, bool VisionReady, bool PermissivesReady, bool MaterialPresent);
public sealed record OperationKey(Guid Session, long Cycle, long Operation, StepKind Step);
public sealed record StationCommand(OperationKey Key, WorkOrder Order, Guid? FrameId, Verdict? Judgment);
public sealed record StationReply(OperationKey Key, string Workpiece, string RecipeVersion, bool Success, Guid? FrameId, Verdict? Judgment);
public sealed record StationTerminal(Guid Session, long Cycle, WorkOrder Order, CycleOutcome Outcome, string Reason, Guid? FrameId, Verdict? Judgment);
public sealed record RecoveryEvidence(Guid Session, long Cycle, long RecoveryVersion, bool CommunicationReady,
    bool MotionIdle, bool VisionIdle, bool MaterialCleared, bool ResultChannelReconciled, bool PermissivesReady);
public sealed record StationEvent(long Sequence, long Cycle, StationStage Stage, string Event, string Reason,
    WorkOrder? Order, OperationKey? Operation, Guid? FrameId, Verdict? Judgment);
public sealed record StationSnapshot(Guid Session, long Cycle, StationStage Stage, WorkOrder? Order, StationCommand? Pending,
    bool CommandTaken, Guid? FrameId, Verdict? Judgment, StationTerminal? Terminal, bool TerminalConsumed, long RecoveryVersion,
    IReadOnlyList<StationEvent> Journal);
public sealed record StageBudgets(TimeSpan Move, TimeSpan Capture, TimeSpan Inspect, TimeSpan ResultAck)
{
    public static StageBudgets Default { get; } = new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1));
    internal TimeSpan For(StepKind step) => step switch
    { StepKind.Move => Move, StepKind.Capture => Capture, StepKind.Inspect => Inspect, StepKind.PublishResult => ResultAck, _ => throw new ArgumentException("Unknown step.") };
    internal void Validate()
    {
        foreach (TimeSpan time in new[] { Move, Capture, Inspect, ResultAck })
            if (time <= TimeSpan.Zero || time > TimeSpan.FromMinutes(2)) throw new ArgumentException("Teaching stage budgets must be positive and <=2minutes.");
    }
}

// Event reducer only: emits simulated commands, NEVER invokes a PLC, card, camera or model.
public sealed class StationController
{
    private readonly object gate = new();
    private readonly TimeProvider clock;
    private readonly StageBudgets budgets;
    private readonly Queue<StationEvent> journal = new();
    private StationStage stage = StationStage.Idle;
    private WorkOrder? order;
    private StationCommand? pending;
    private StationTerminal? terminal;
    private Guid? frame;
    private Verdict? judgment;
    private long cycle, operation, highNumber, issuedAt, recoveryVersion, eventNumber;
    private bool taken, consumed = true;
    public Guid Session { get; } = Guid.NewGuid();
    public Guid Producer { get; }
    public StationController(Guid producer, TimeProvider? clock = null, StageBudgets? budgets = null)
    {
        if (producer == Guid.Empty) throw new ArgumentException("Producer required.");
        Producer = producer; this.clock = clock ?? TimeProvider.System; this.budgets = budgets ?? StageBudgets.Default;
        this.budgets.Validate(); Log("Created", "Offline-only controller.");
    }
    public StationSnapshot Snapshot
    {
        get
        {
            lock (gate) return new(Session, cycle, stage, order, pending, taken, frame, judgment,
                terminal, consumed, recoveryVersion, Array.AsReadOnly(journal.ToArray()));
        }
    }
    public Admission TryStart(WorkOrder work, StartEvidence ready)
    {
        if (work is null || work.Producer != Producer || work.Number <= 0 || !Text(work.Workpiece) || !Text(work.RecipeVersion) || ready is null)
            return Admission.Invalid;
        lock (gate)
        {
            if (stage is StationStage.Closing or StationStage.Closed) return Admission.Closed;
            if (order is not null && work.Number == order.Number)
                return ReferenceEquals(work, order) ? Admission.AlreadyTracked : Admission.Conflict;
            if (work.Number <= highNumber) return Admission.Stale;
            if (stage != StationStage.Idle) return Admission.Busy;
            if (!ready.CommunicationReady || !ready.MotionReady || !ready.VisionReady || !ready.PermissivesReady || !ready.MaterialPresent)
                return Admission.NotReady;
            order = work; highNumber = work.Number; cycle = checked(cycle + 1); terminal = null; consumed = false; frame = null; judgment = null;
            Issue(StepKind.Move); Log("Accepted", "Captured workpiece and recipe version."); return Admission.Accepted;
        }
    }
    public StationCommand? TryTakeCommand()
    {
        lock (gate)
        {
            Expire();
            if (!Waiting || pending is null || taken) return null;
            taken = true; Log("CommandTaken", pending.Key.Step.ToString()); return pending;
        }
    }
    public void Tick() { lock (gate) Expire(); }
    public ReplyDisposition Receive(StationReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        lock (gate)
        {
            Expire();
            if (pending is null) { Log("ReplyIgnored", "No outstanding command."); return ReplyDisposition.NoPending; }
            if (!taken || reply.Key != pending.Key || reply.Workpiece != pending.Order.Workpiece || reply.RecipeVersion != pending.Order.RecipeVersion)
            { Log("ReplyIgnored", "Session/cycle/operation/step/workpiece/recipe mismatch or command not taken."); return ReplyDisposition.IgnoredCorrelation; }
            if (!PayloadMatches(reply, pending))
            {
                if (Waiting) Latch(StationStage.Faulted, CycleOutcome.Faulted, "ReplyPayloadMismatch");
                Log("ReplyRejected", "Frame or verdict mismatch; obligation remains unsettled."); return ReplyDisposition.RejectedPayload;
            }
            StepKind finished = pending.Key.Step;
            pending = null; taken = false;
            if (!Waiting)
            { Log("LateReplyDrained", $"op={reply.Key.Operation} {finished}; success={reply.Success}; frame={reply.FrameId}; judgment={reply.Judgment}"); FinishClose(); return ReplyDisposition.DrainedLate; }
            if (!reply.Success)
            { Latch(StationStage.Faulted, CycleOutcome.Faulted, $"StepFailed:{finished}"); return ReplyDisposition.TerminalCreated; }
            switch (finished)
            {
                case StepKind.Move: Issue(StepKind.Capture); break;
                case StepKind.Capture: frame = reply.FrameId; Issue(StepKind.Inspect); break;
                case StepKind.Inspect: judgment = reply.Judgment; Issue(StepKind.PublishResult); break;
                case StepKind.PublishResult:
                    stage = StationStage.Completed;
                    terminal = new(Session, cycle, order!, CycleOutcome.Completed, "MatchingResultAck", frame, judgment);
                    Log("CycleCompleted", "Valid OK or NG result acknowledged by simulated receiver."); return ReplyDisposition.TerminalCreated;
            }
            return ReplyDisposition.Advanced;
        }
    }
    public bool RecordUncertain(OperationKey key, string code)
    {
        if (!Text(code)) throw new ArgumentException("Bounded uncertainty code required.");
        lock (gate)
        {
            if (pending is null || !taken || pending.Key != key) return false;
            if (Waiting) Latch(StationStage.Faulted, CycleOutcome.Faulted, $"PortUncertain:{code}");
            else Log("PortUncertain", code);
            return true; // Deliberately does NOT settle pending.
        }
    }
    public void Stop()
    {
        lock (gate)
        {
            if (stage is StationStage.Closing or StationStage.Closed or StationStage.Faulted or StationStage.Stopped) return;
            Latch(StationStage.Stopped, CycleOutcome.Stopped, "SoftwareStopRequested");
        }
    }
    public bool ConsumeTerminal(StationTerminal receipt)
    {
        lock (gate)
        {
            if (receipt is null || !ReferenceEquals(receipt, terminal) || consumed) return false;
            consumed = true; Log("TerminalConsumed", "Local observation only, not PLC or physical acceptance."); FinishClose(); return true;
        }
    }
    public bool ReleaseCompletedCycle(RecoveryEvidence evidence)
    {
        lock (gate)
        {
            if (stage != StationStage.Completed || !CanRecover(evidence)) return false;
            ClearCycle("Released", "External declarations allow next cycle."); return true;
        }
    }
    public bool Reset(RecoveryEvidence evidence)
    {
        lock (gate)
        {
            if (stage is not (StationStage.Faulted or StationStage.Stopped) || !CanRecover(evidence)) return false;
            ClearCycle("Reset", "Logical reset only; no homing, motion or result resend."); return true;
        }
    }
    public void RequestClose()
    {
        lock (gate)
        {
            if (stage is StationStage.Closing or StationStage.Closed) return;
            if (order is not null && terminal is null)
                terminal = new(Session, cycle, order, CycleOutcome.Stopped, "CloseRequested", frame, judgment);
            if (pending is not null && !taken) { Log("UnsentWithdrawn", pending.Key.Step.ToString()); pending = null; }
            stage = StationStage.Closing; Log("Closing", "No new commands; wait for taken command and local terminal consumption."); FinishClose();
        }
    }
    private bool CanRecover(RecoveryEvidence? e) => e is not null && e.Session == Session && e.Cycle == cycle &&
        e.RecoveryVersion == recoveryVersion && pending is null && consumed && e.CommunicationReady && e.MotionIdle && e.VisionIdle &&
        e.MaterialCleared && e.ResultChannelReconciled && e.PermissivesReady;
    private void ClearCycle(string name, string reason)
    { order = null; pending = null; terminal = null; consumed = true; taken = false; frame = null; judgment = null; stage = StationStage.Idle; Log(name, reason); }
    private void FinishClose()
    { if (stage == StationStage.Closing && pending is null && consumed) { stage = StationStage.Closed; Log("Closed", "Local obligations settled; no physical stop proof."); } }
    private bool Waiting => stage is StationStage.AwaitingMove or StationStage.AwaitingCapture or StationStage.AwaitingInspection or StationStage.AwaitingResultAck;
    private void Issue(StepKind step)
    {
        pending = new(new(Session, cycle, checked(++operation), step), order!, frame, judgment); taken = false; issuedAt = clock.GetTimestamp();
        stage = step switch { StepKind.Move => StationStage.AwaitingMove, StepKind.Capture => StationStage.AwaitingCapture,
            StepKind.Inspect => StationStage.AwaitingInspection, _ => StationStage.AwaitingResultAck };
        Log("CommandIssued", step.ToString());
    }
    private void Expire()
    {
        if (!Waiting || pending is null) return;
        TimeSpan elapsed = clock.GetElapsedTime(issuedAt, clock.GetTimestamp());
        if (elapsed < TimeSpan.Zero) Latch(StationStage.Faulted, CycleOutcome.Faulted, "ClockMovedBackwards");
        else if (elapsed >= budgets.For(pending.Key.Step)) Latch(StationStage.Faulted, CycleOutcome.Faulted, $"TimedOut:{pending.Key.Step}");
    }
    private void Latch(StationStage state, CycleOutcome outcome, string reason)
    {
        recoveryVersion = checked(recoveryVersion + 1);
        if (order is not null && terminal is null) terminal = new(Session, cycle, order, outcome, reason, frame, judgment);
        if (pending is not null && !taken) { Log("UnsentWithdrawn", pending.Key.Step.ToString()); pending = null; }
        stage = state; Log("Latched", reason);
    }
    private static bool PayloadMatches(StationReply reply, StationCommand command) => command.Key.Step switch
    {
        StepKind.Move => reply.FrameId is null && reply.Judgment is null,
        StepKind.Capture => reply.Judgment is null && (reply.Success ? reply.FrameId is { } id && id != Guid.Empty : reply.FrameId is null),
        StepKind.Inspect => reply.FrameId == command.FrameId && (reply.Success ? reply.Judgment is { } v && Enum.IsDefined(v) : reply.Judgment is null),
        StepKind.PublishResult => reply.FrameId == command.FrameId && reply.Judgment == command.Judgment,
        _ => false
    };
    private void Log(string name, string reason)
    { if (journal.Count == 128) journal.Dequeue(); journal.Enqueue(new(checked(++eventNumber), cycle, stage, name, reason, order, pending?.Key, frame, judgment)); }
    private static bool Text(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 64 && text == text.Trim() && !text.Any(char.IsControl);
}
