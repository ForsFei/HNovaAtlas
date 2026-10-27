namespace IndustrialRobotics;

public static class RobotTaskChecks
{
    private static int count;
    private static readonly RobotBinding Binding = new(Guid.Parse("47b63c07-f544-4645-a86f-51450e5f126a"), 1, 2, 3);
    private static readonly RobotTaskResult Ok = new(1, "inspection-complete", RobotVerdict.Ok);
    private static readonly RobotTaskResult Ng = new(1, "inspection-complete", RobotVerdict.Ng);
    private static RobotMailboxFrame Frame(double time, RobotRemotePhase phase = RobotRemotePhase.Empty,
        RobotTaskRequest? job = null, RobotTaskResult? result = null) =>
        new(new(Binding, time, RobotMode.Automatic, true, true,
            phase is RobotRemotePhase.Empty or RobotRemotePhase.Completed or RobotRemotePhase.Rejected,
            true, true), phase, job, result, true);
    private static RobotTaskSession Session(double budget = 5) => new(Binding, 10, 3, startupReconciled: true, taskBudget: budget);
    private static (RobotTaskSession Session, RobotTaskRequest Job) Published(double budget = 5)
    {
        RobotTaskSession s = Session(budget); s.Observe(Frame(0), 0);
        RobotTaskRequest job = s.Prepare("part-A", .01)!;
        _ = s.TakePublish(.02); _ = s.RecordPublish(job.Key, RobotPublishEvidence.Acknowledged, .03);
        return (s, job);
    }
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine($"PASS {++count}: {label}");
    }
    private static void Reject(Action action, string label)
    {
        try { action(); } catch (ArgumentException) { Check(true, label); return; }
        throw new InvalidOperationException($"Not rejected: {label}");
    }
    public static void Run()
    {
        count = 0;
        RobotTaskSession lockedStartup = new(Binding, 10, 3);
        _ = lockedStartup.Observe(Frame(0), 0);
        Check(lockedStartup.Prepare("part-A", .01) is null, "Default startup cannot auto-adopt an empty-looking controller");
        RobotTaskSession s = Session();
        Check(s.Prepare("part-A", 0) is null, "No preparation before a qualified remote snapshot");
        Check(s.Observe(Frame(0), 0), "Explicit fixture idle snapshot observed");
        RobotTaskRequest job = s.Prepare("part-A", .01)!;
        Check(job.Key.PcSession == s.PcSession && job.Key.Number == 1 && job.JobId == 10
            && job.RecipeRevision == 3 && job.Binding == Binding, "Prepared request captures approved identity/payload");
        Check(s.Read(.01).Current?.Stage == RobotJobStage.Prepared, "Prepare is not publication");
        Check(s.Prepare("part-B", .01) is null, "A second click cannot overwrite a retained request");
        Check(s.TakePublish(.02) == job && s.TakePublish(.02) is null, "Publication intent taken only once");
        Check(!s.RecordPublish(job.Key with { Number = 99 }, RobotPublishEvidence.Acknowledged, .03), "Foreign publication receipt ignored");
        Check(s.RecordPublish(job.Key, RobotPublishEvidence.Acknowledged, .03), "Transport receipt recorded separately");
        Check(s.Read(.03).Current is { Stage: RobotJobStage.AwaitingAcceptance, TransportAcknowledged: true }, "Write receipt does not mean accepted/running/completed");
        Check(s.TakeResultAck(.03) is null, "No result acknowledgement before a terminal result");
        Check(s.Observe(Frame(.04, RobotRemotePhase.Accepted, job), .04)
            && s.Read(.04).Current?.Stage == RobotJobStage.Accepted, "Matched acceptance");
        Check(s.Observe(Frame(.05, RobotRemotePhase.Running, job), .05)
            && s.Read(.05).Current?.Stage == RobotJobStage.Running, "Matched running; program idle need not stay true");
        Check(s.Observe(Frame(.06, RobotRemotePhase.Completed, job, Ng), .06)
            && s.Read(.06).Current is { Stage: RobotJobStage.Completed, Result.Verdict: RobotVerdict.Ng }, "Completed NG is a process result, not execution failure");
        Check(s.Observe(Frame(.07, RobotRemotePhase.Completed, job, Ng), .07), "Identical retained terminal result is stable");
        RobotResultAck ack = s.TakeResultAck(.08)!;
        Check(ack.Key == job.Key && ack.Result == Ng, "Result acknowledgement binds identity and full result revision/value");
        Check(s.TakeResultAck(.08) is null, "Result ACK intent taken once");
        Check(s.Read(.08).Current is not null && s.Prepare("part-B", .08) is null, "ACK take is not evidence of remote clear");
        Check(s.Observe(Frame(.09), .09) && s.Read(.09).Current is null
            && s.Read(.09).LastReleased?.Result == Ng, "Verified clear after ACK releases slot, preserves last history");
        RobotTaskRequest next = s.Prepare("part-B", .1)!;
        Check(next.Key.Number == 2 && next.Key != job.Key && s.Read(.1).Current?.Stage == RobotJobStage.Prepared, "Next task needs a fresh explicit prepare; no automatic publication");

        var a = Published();
        Check(!a.Session.Observe(Frame(.04, RobotRemotePhase.Accepted, a.Job with { Workpiece = "changed" }), .04)
            && a.Session.Read(.04).ReconciliationRequired, "Same identity changed payload conflicts");
        a = Published();
        Check(!a.Session.Observe(Frame(.04, RobotRemotePhase.Completed, a.Job with { Key = a.Job.Key with { PcSession = Guid.NewGuid() } }, Ok), .04), "Old/foreign PC session cannot complete this job");
        a = Published();
        Check(!a.Session.Observe(Frame(.04, RobotRemotePhase.Completed, a.Job, Ok with { Revision = 0 }), .04), "Missing result revision refused");
        a = Published();
        Check(!a.Session.Observe(Frame(.04, RobotRemotePhase.Completed, a.Job, Ok with { Verdict = RobotVerdict.Unspecified }), .04), "Unspecified verdict cannot masquerade as completed process");
        a = Published();
        Check(!a.Session.Observe(Frame(.04, RobotRemotePhase.Running, a.Job, Ok), .04), "Running snapshot cannot carry terminal data");
        a = Published();
        Check(!a.Session.Observe(Frame(.04, RobotRemotePhase.Empty, a.Job), .04), "Empty slot cannot carry a task");
        a = Published();
        Check(!a.Session.Observe(Frame(.04), .04) && a.Session.Read(.04).Current?.Stage == RobotJobStage.Unknown, "Vanished record is unknown, not not-executed");
        a = Published();
        Check(a.Session.Observe(Frame(.04, RobotRemotePhase.Completed, a.Job, Ok), .04), "Qualified retained terminal can be observed even if polling missed running");
        Check(!a.Session.Observe(Frame(.05, RobotRemotePhase.Completed, a.Job, Ng), .05)
            && a.Session.Read(.05).Current?.Result == Ok, "Conflicting result cannot overwrite previous terminal history");
        a = Published();
        _ = a.Session.Observe(Frame(.04, RobotRemotePhase.Running, a.Job), .04);
        Check(!a.Session.Observe(Frame(.05, RobotRemotePhase.Accepted, a.Job), .05), "Running cannot regress to accepted");
        a = Published();
        _ = a.Session.Observe(Frame(.04, RobotRemotePhase.Running, a.Job), .04);
        a.Session.Invalidate();
        Check(!a.Session.Observe(Frame(.05, RobotRemotePhase.Rejected, a.Job, new(1, "bad-recipe")), .05), "Uncertainty does not erase earlier running evidence for rejection checks");
        a = Published();
        Check(a.Session.Observe(Frame(.04, RobotRemotePhase.Held, a.Job), .04)
            && a.Session.Read(.04) is { ReconciliationRequired: true, Current.Stage: RobotJobStage.Held }, "Held is retained and locked, no continue emitted");
        Check(!a.Session.Observe(Frame(.05, RobotRemotePhase.Running, a.Job), .05)
            && a.Session.TakePublish(.05) is null, "Held does not auto-resume through fresh running input");
        a = Published();
        Check(a.Session.Observe(Frame(.04, RobotRemotePhase.Failed, a.Job, new(1, "grip-fault")), .04)
            && a.Session.TakeResultAck(.04) is null, "Execution failure requires reconciliation, not ordinary successful release");
        a = Published();
        Check(a.Session.Observe(Frame(.04, RobotRemotePhase.Rejected, a.Job, new(1, "recipe-rejected")), .04)
            && a.Session.TakeResultAck(.04) is not null, "Explicit pre-execution rejection can have its record acknowledged");

        s = Session(); _ = s.Observe(Frame(0), 0); job = s.Prepare("part-A", .01)!; _ = s.TakePublish(.02);
        Check(s.RecordPublish(job.Key, RobotPublishEvidence.Unknown, .03)
            && s.Read(.03) is { ReconciliationRequired: true, HadUncertainty: true, Current.Stage: RobotJobStage.Unknown }, "Lost publication receipt freezes writes");
        Check(s.Observe(Frame(.04, RobotRemotePhase.Completed, job, Ok), .04)
            && s.Read(.04) is { ReconciliationRequired: true, HadUncertainty: true, Current.Stage: RobotJobStage.Completed }, "Late qualified original result updates history but never unlocks");
        Check(s.TakeResultAck(.04) is null && s.Prepare("part-B", .04) is null && s.TakePublish(.04) is null, "No ACK/replay/new task after uncertain publication");
        s = Session(); _ = s.Observe(Frame(0), 0); job = s.Prepare("part-A", .01)!; _ = s.TakePublish(.02);
        Check(s.RecordPublish(job.Key, RobotPublishEvidence.NotAttempted, .03)
            && s.Read(.03).Current?.Stage == RobotJobStage.NotPublished, "Explicit local not-attempted is not a physical standstill claim");
        Check(!s.Observe(Frame(.04, RobotRemotePhase.Accepted, job), .04), "Remote task contradicting not-published boundary refused");

        foreach (string fault in new[] { "quality", "qualified", "mode", "remote", "alarm", "permit", "binding", "future", "nan", "repeat", "enum" })
        {
            a = Published(); RobotMailboxFrame good = Frame(.04, RobotRemotePhase.Accepted, a.Job);
            RobotMailboxFrame bad = fault switch
            {
                "quality" => good with { Context = good.Context with { Valid = false } },
                "qualified" => good with { Qualified = false },
                "mode" => good with { Context = good.Context with { Mode = RobotMode.Teach } },
                "remote" => good with { Context = good.Context with { RemoteSelected = false } },
                "alarm" => good with { Context = good.Context with { NoFaultObserved = false } },
                "permit" => good with { Context = good.Context with { ProcessPermit = false } },
                "binding" => good with { Context = good.Context with { Binding = Binding with { Revision = 2 } } },
                "future" => good with { Context = good.Context with { SampledAt = 1 } },
                "nan" => good with { Context = good.Context with { SampledAt = double.NaN } },
                "repeat" => good with { Context = good.Context with { SampledAt = 0 } },
                _ => good with { Phase = (RobotRemotePhase)99 }
            };
            _ = a.Session.Observe(bad, .04);
            Check(a.Session.Read(.04).ReconciliationRequired && a.Session.TakePublish(.04) is null, $"Bad context/evidence locks writes: {fault}");
        }
        a = Published();
        _ = a.Session.Observe(Frame(.3, RobotRemotePhase.Completed, a.Job, Ok), .3);
        Check(a.Session.Read(.3) is { ReconciliationRequired: true, Current.Stage: RobotJobStage.Completed }, "Fresh frame after unobserved coverage gap may recover history only");
        a = Published();
        _ = a.Session.Observe(Frame(.1, RobotRemotePhase.Accepted, a.Job), .1);
        _ = a.Session.Observe(Frame(.2, RobotRemotePhase.Running, a.Job), .4);
        Check(a.Session.Read(.4).ReconciliationRequired, "Fresh-enough new sample cannot conceal expiry of previous observation");
        a = Published(.15);
        _ = a.Session.Observe(Frame(.1, RobotRemotePhase.Running, a.Job), .1);
        Check(a.Session.Read(.2).Current?.Stage == RobotJobStage.Unknown, "Task budget can expire while snapshot is still fresh; no stop sent");
        s = Session(); _ = s.Observe(Frame(0), 0); _ = s.Prepare("part-A", .01);
        Check(s.TakePublish(.3) is null && s.Read(.3).Current?.Stage == RobotJobStage.NotPublished, "Pre-publication expiry does not create a publication intent");
        a = Published(); _ = a.Session.Observe(Frame(.04, RobotRemotePhase.Completed, a.Job, Ok), .04);
        a.Session.Invalidate();
        Check(a.Session.Read(.04).Current?.Result == Ok && a.Session.TakeResultAck(.04) is null, "Completed history survives invalidation; no fresh write permission");
        a = Published(.15); _ = a.Session.Observe(Frame(.04, RobotRemotePhase.Completed, a.Job, Ok), .04);
        _ = a.Session.TakeResultAck(.05);
        Check(a.Session.Read(.21) is { ReconciliationRequired: true, HadUncertainty: true, Current.Result.Verdict: RobotVerdict.Ok }, "ACK-clear timeout retains original completion and records release uncertainty");
        a = Published(); _ = a.Session.Observe(Frame(.04, RobotRemotePhase.Completed, a.Job, Ok), .04);
        _ = a.Session.TakeResultAck(.05);
        Check(a.Session.Observe(Frame(.06), .06) && a.Session.Read(.06).LastReleased?.Result == Ok, "Lost ACK receipt can still be reconciled by a qualified empty slot in same uninterrupted session");
        RobotTaskSession restarted = Session();
        Check(!restarted.Observe(Frame(.07, RobotRemotePhase.Completed, a.Job, Ok), .07)
            && restarted.PcSession != a.Job.Key.PcSession && restarted.Prepare("part-A", .07) is null, "New PC instance never adopts another session's retained task");
        s = Session();
        RobotMailboxFrame busyProgram = Frame(0);
        _ = s.Observe(busyProgram with { Context = busyProgram.Context with { ProgramIdle = false } }, 0);
        Check(s.Prepare("part-A", .01) is null, "Empty mailbox is not enough if program is not idle");
        s = Session(); _ = s.Observe(Frame(0), 0);
        Reject(() => s.Prepare(" ", .01), "Reject empty workpiece identity");
        a = Published();
        Check(!a.Session.RecordPublish(a.Job.Key, (RobotPublishEvidence)99, .04), "Receipt cannot be recorded twice after acknowledgement");
        s = Session(); _ = s.Observe(Frame(0), 0); job = s.Prepare("part-A", .01)!; _ = s.TakePublish(.02);
        Check(!s.RecordPublish(job.Key, (RobotPublishEvidence)99, .03)
            && s.Read(.03).ReconciliationRequired, "Undefined publication evidence locks unresolved attempt");
        a = Published();
        Check(a.Session.Read(double.NaN).ReconciliationRequired, "Invalid clock locks the session");
        Reject(() => new RobotTaskSession(Binding, 0, 3), "Reject unapproved job id");
        Reject(() => new RobotTaskSession(Binding, 10, 3, maxAge: double.NaN), "Reject invalid age policy");
        Console.WriteLine($"{count} offline robot-task checks passed; no robot program, SDK, wire protocol or physical motion.");
    }
    public static void Demo()
    {
        RobotTaskSession s = Session(); _ = s.Observe(Frame(0), 0);
        RobotTaskRequest job = s.Prepare("part-A", .01)!; _ = s.TakePublish(.02);
        _ = s.RecordPublish(job.Key, RobotPublishEvidence.Unknown, .03);
        Console.WriteLine($"Publication receipt lost: {s.Read(.03).Current?.Stage}");
        _ = s.Observe(Frame(.04, RobotRemotePhase.Completed, job, Ng), .04);
        RobotSessionView view = s.Read(.04);
        Console.WriteLine($"Original task queried: {view.Current?.Stage}, verdict={view.Current?.Result?.Verdict}");
        Console.WriteLine($"ReconciliationRequired={view.ReconciliationRequired}, HadUncertainty={view.HadUncertainty}, ACK={s.TakeResultAck(.04) is not null}");
        Console.WriteLine("Scripted memory reports only. No start, continue, stop, reset or network write.");
    }
}
