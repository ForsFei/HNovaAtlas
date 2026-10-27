namespace IndustrialRobotics;

public readonly record struct RobotTaskKey(Guid PcSession, uint Number);
public sealed record RobotTaskRequest(RobotTaskKey Key, string Workpiece, ushort JobId,
    uint RecipeRevision, RobotBinding Binding);
public enum RobotVerdict { Unspecified, Ok, Ng }
public sealed record RobotTaskResult(uint Revision, string Code, RobotVerdict Verdict = RobotVerdict.Unspecified);
public enum RobotRemotePhase { Empty, Accepted, Running, Held, Completed, Rejected, Failed }
public sealed record RobotMailboxFrame(RobotContextSample Context, RobotRemotePhase Phase,
    RobotTaskRequest? Job = null, RobotTaskResult? Result = null, bool Qualified = false);
public enum RobotJobStage { Prepared, PublishTaken, AwaitingAcceptance, Accepted, Running, Held,
    Completed, Rejected, Failed, NotPublished, Unknown }
public enum RobotPublishEvidence { NotAttempted, Acknowledged, Unknown }
public sealed record RobotTaskView(RobotTaskRequest Request, RobotJobStage Stage,
    RobotTaskResult? Result = null, bool TransportAcknowledged = false, bool AckTaken = false);
public sealed record RobotResultAck(RobotTaskKey Key, RobotTaskResult Result);
public sealed record RobotSessionView(Guid PcSession, bool ReconciliationRequired, bool HadUncertainty,
    string Reason, RobotTaskView? Current, RobotTaskView? LastReleased);

// PC-side, serialized, one retained job. Consumes normalized, qualified remote reports.
// No robot program, wire protocol, SDK, motion, stop, reset, resume or recovery implementation.
public sealed class RobotTaskSession
{
    private readonly RobotBinding expected;
    private readonly ushort jobId;
    private readonly uint recipeRevision;
    private readonly double maxAge, taskBudget;
    private RobotMailboxFrame? lastFrame;
    private RobotTaskView? current, lastReleased;
    private RobotRemotePhase? lastTaskPhase;
    private double lastNow;
    private double? takenAt, ackAt;
    private uint sequence;
    private bool locked, hadUncertainty;
    private string reason = "No qualified remote snapshot";
    public Guid PcSession { get; } = Guid.NewGuid();

    public RobotTaskSession(RobotBinding reconciledBinding, ushort approvedJobId, uint approvedRecipeRevision,
        bool startupReconciled = false, double maxAge = .25, double taskBudget = 5)
    {
        if (reconciledBinding.ControllerBoot == Guid.Empty || reconciledBinding.Revision == 0
            || approvedJobId == 0 || approvedRecipeRevision == 0 || !double.IsFinite(maxAge)
            || maxAge <= 0 || !double.IsFinite(taskBudget) || taskBudget <= 0)
            throw new ArgumentException("Explicit binding, one approved job/recipe, and positive budgets required.");
        expected = reconciledBinding; jobId = approvedJobId; recipeRevision = approvedRecipeRevision;
        this.maxAge = maxAge; this.taskBudget = taskBudget;
        if (!startupReconciled) Lock("External startup reconciliation not declared");
    }

    private static bool Terminal(RobotJobStage stage) => stage is RobotJobStage.Completed or RobotJobStage.Rejected or RobotJobStage.Failed;
    private void Lock(string cause)
    {
        locked = true; reason = cause;
        if (current?.AckTaken == true) hadUncertainty = true;
        if (current?.Stage == RobotJobStage.Prepared) current = current with { Stage = RobotJobStage.NotPublished };
        else if (current is not null && !Terminal(current.Stage) && current.Stage != RobotJobStage.NotPublished)
        {
            hadUncertainty = true;
            if (current.Stage != RobotJobStage.Held) current = current with { Stage = RobotJobStage.Unknown };
        }
    }
    private bool CheckTime(double now)
    {
        if (!double.IsFinite(now) || now < lastNow) { Lock("Invalid/backwards monotonic clock"); return false; }
        lastNow = now;
        if (lastFrame is not null && now - lastFrame.Context.SampledAt > maxAge)
            Lock("Observation coverage expired; no automatic recovery");
        if (takenAt.HasValue && current is not null && !Terminal(current.Stage) && current.Stage != RobotJobStage.NotPublished
            && now - takenAt.Value > taskBudget) Lock("Local task observation budget expired; no stop sent");
        if (ackAt.HasValue && current is not null && now - ackAt.Value > taskBudget)
            Lock("Result-clear observation budget expired; completion history retained");
        return true;
    }
    private static bool OrdinaryWriteContext(RobotContextSample c) => c.Mode == RobotMode.Automatic
        && c.RemoteSelected && c.NoFaultObserved && c.ProcessPermit;
    private bool CanWrite(double now) => CheckTime(now) && !locked && lastFrame is not null
        && OrdinaryWriteContext(lastFrame.Context);

    public RobotTaskRequest? Prepare(string workpiece, double now)
    {
        if (!CanWrite(now) || current is not null || lastFrame!.Phase != RobotRemotePhase.Empty
            || !lastFrame.Context.ProgramIdle) return null;
        if (string.IsNullOrWhiteSpace(workpiece) || workpiece.Length > 80)
            throw new ArgumentException("A bounded workpiece identity is required.");
        if (sequence == uint.MaxValue) { Lock("Request numbers exhausted; never wrap"); return null; }
        RobotTaskRequest request = new(new(PcSession, ++sequence), workpiece, jobId, recipeRevision, expected);
        current = new(request, RobotJobStage.Prepared); takenAt = null; ackAt = null; lastTaskPhase = null;
        return request;
    }
    public RobotTaskRequest? TakePublish(double now)
    {
        if (!CanWrite(now) || current?.Stage != RobotJobStage.Prepared
            || lastFrame!.Phase != RobotRemotePhase.Empty || !lastFrame.Context.ProgramIdle) return null;
        takenAt = now; current = current with { Stage = RobotJobStage.PublishTaken };
        return current.Request; // Take once; not evidence that bytes or a physical action were sent.
    }
    public bool RecordPublish(RobotTaskKey key, RobotPublishEvidence evidence, double now)
    {
        if (!CheckTime(now) || locked || current?.Request.Key != key || current.Stage != RobotJobStage.PublishTaken) return false;
        if (!Enum.IsDefined(evidence)) { Lock("Invalid publication evidence"); return false; }
        if (evidence == RobotPublishEvidence.NotAttempted)
        {
            current = current with { Stage = RobotJobStage.NotPublished };
            Lock("Publisher declares not attempted; review before another task");
        }
        else if (evidence == RobotPublishEvidence.Unknown) Lock("Publication outcome unknown; query original identity only");
        else current = current with { Stage = RobotJobStage.AwaitingAcceptance, TransportAcknowledged = true };
        return true;
    }

    public bool Observe(RobotMailboxFrame frame, double now)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!CheckTime(now)) return false;
        RobotContextSample c = frame.Context;
        if (c is null || !frame.Qualified || !c.Valid || c.Binding != expected
            || !double.IsFinite(c.SampledAt) || c.SampledAt < 0 || c.SampledAt > now
            || now - c.SampledAt > maxAge || (lastFrame is not null && c.SampledAt <= lastFrame.Context.SampledAt)
            || !Enum.IsDefined(frame.Phase))
        { Lock("Unqualified, stale, mismatched or malformed remote snapshot"); return false; }
        if (!WellFormed(frame)) { Lock("Inconsistent remote slot/result shape"); return false; }
        // Fresh input after a gap can support HISTORY lookup, but never unlocks this session.
        lastFrame = frame;
        if (!OrdinaryWriteContext(c)) Lock("Mode, remote source, fault or process context lost");
        if (!locked) reason = $"Qualified {frame.Phase} snapshot; ordinary application evidence only";
        if (frame.Phase == RobotRemotePhase.Empty)
        {
            if (current is null || current.Stage == RobotJobStage.Prepared) return true;
            if (!locked && current.AckTaken && Terminal(current.Stage))
            {
                lastReleased = current; current = null; takenAt = null; ackAt = null; lastTaskPhase = null;
                return true;
            }
            Lock("Remote slot empty without a verified result-release sequence"); return false;
        }
        if (current is null || frame.Job != current.Request)
        { Lock("Unowned task or changed identity/payload; never adopt/replay"); return false; }
        if (!takenAt.HasValue || current.Stage == RobotJobStage.NotPublished)
        { Lock("Remote task contradicts local not-published boundary"); return false; }
        if (Terminal(current.Stage))
        {
            RobotJobStage reported = StageFor(frame.Phase);
            if (current.Stage == reported && current.Result == frame.Result) return true;
            Lock("Retained terminal result changed or regressed"); return false;
        }
        if (frame.Phase == RobotRemotePhase.Accepted && lastTaskPhase is RobotRemotePhase.Running or RobotRemotePhase.Held)
        { Lock("Remote task regressed to acceptance"); return false; }
        if (frame.Phase == RobotRemotePhase.Running && lastTaskPhase == RobotRemotePhase.Held)
        { Lock("Resume not implemented; reconciliation required"); return false; }
        if (frame.Phase == RobotRemotePhase.Rejected && lastTaskPhase is RobotRemotePhase.Running or RobotRemotePhase.Held)
        { Lock("Rejection after execution is not a valid no-execution result"); return false; }
        lastTaskPhase = frame.Phase;
        current = current with { Stage = StageFor(frame.Phase), Result = frame.Result };
        if (frame.Phase == RobotRemotePhase.Held) Lock("Held task is not completed; no continue command");
        else if (frame.Phase == RobotRemotePhase.Failed) Lock("Robot-program failure; physical effects require reconciliation");
        return true;
    }

    private static bool WellFormed(RobotMailboxFrame frame)
    {
        if (frame.Phase == RobotRemotePhase.Empty) return frame.Job is null && frame.Result is null;
        if (frame.Job is null || frame.Job.Key.PcSession == Guid.Empty || frame.Job.Key.Number == 0) return false;
        bool terminal = frame.Phase is RobotRemotePhase.Completed or RobotRemotePhase.Rejected or RobotRemotePhase.Failed;
        if (!terminal) return frame.Result is null;
        RobotTaskResult? r = frame.Result;
        return r is not null && r.Revision > 0 && !string.IsNullOrWhiteSpace(r.Code) && r.Code.Length <= 80
            && (frame.Phase == RobotRemotePhase.Completed ? r.Verdict is RobotVerdict.Ok or RobotVerdict.Ng
                : r.Verdict == RobotVerdict.Unspecified);
    }
    private static RobotJobStage StageFor(RobotRemotePhase phase) => phase switch
    {
        RobotRemotePhase.Accepted => RobotJobStage.Accepted,
        RobotRemotePhase.Running => RobotJobStage.Running,
        RobotRemotePhase.Held => RobotJobStage.Held,
        RobotRemotePhase.Completed => RobotJobStage.Completed,
        RobotRemotePhase.Rejected => RobotJobStage.Rejected,
        RobotRemotePhase.Failed => RobotJobStage.Failed,
        _ => throw new ArgumentException("No task stage for empty slot")
    };
    public RobotResultAck? TakeResultAck(double now)
    {
        if (!CanWrite(now) || current is null || current.Result is null || current.AckTaken
            || current.Stage is not (RobotJobStage.Completed or RobotJobStage.Rejected)
            || lastFrame!.Job != current.Request || lastFrame.Result != current.Result) return null;
        current = current with { AckTaken = true }; ackAt = now;
        return new(current.Request.Key, current.Result); // One local ACK intent, not a network write.
    }
    public RobotSessionView Read(double now)
    {
        _ = CheckTime(now);
        return new(PcSession, locked, hadUncertainty, reason, current, lastReleased);
    }
    public void Invalidate() => Lock("External invalidation; query history, no stop/reset/retry");
}
