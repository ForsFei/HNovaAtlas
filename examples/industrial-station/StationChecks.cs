namespace IndustrialStation;

internal static class StationChecks
{
    private static int count;
    private static StationController New(ManualStationClock? clock = null) => new(Guid.NewGuid(), clock ?? new ManualStationClock());
    private static WorkOrder Order(StationController s, long number = 1) => new(s.Producer, number, $"part-{number}", "recipe-v1");
    private static StationTerminal Terminal(StationController s) => s.Snapshot.Terminal ?? throw new InvalidOperationException("Terminal missing.");
    public static void Run()
    {
        count = 0;
        Reject(() => new StationController(Guid.Empty), "empty producer");
        Reject(() => new StationController(Guid.NewGuid(), budgets: StageBudgets.Default with { Move = TimeSpan.Zero }), "nonpositive deadline");
        StationController s = New(); WorkOrder first = Order(s);
        Check(s.Snapshot.Stage == StationStage.Idle && s.TryTakeCommand() is null, "initial idle, no spontaneous command");
        Check(s.TryStart(first with { Producer = Guid.NewGuid() }, OfflineSimulation.Ready) == Admission.Invalid, "foreign producer");
        Check(s.TryStart(first with { Workpiece = " " }, OfflineSimulation.Ready) == Admission.Invalid, "invalid workpiece");
        Check(s.TryStart(first, OfflineSimulation.Ready with { MaterialPresent = false }) == Admission.NotReady, "no material");
        Check(s.TryStart(first, OfflineSimulation.Ready with { MotionReady = false }) == Admission.NotReady, "not ready no motion");
        Check(s.TryStart(first, OfflineSimulation.Ready) == Admission.Accepted, "accept exactly one cycle");
        Check(s.TryStart(first, OfflineSimulation.Ready) == Admission.AlreadyTracked, "same request idempotent");
        Check(s.TryStart(first with { }, OfflineSimulation.Ready) == Admission.Conflict, "same number copied request conflicts");
        Check(s.TryStart(Order(s, 2), OfflineSimulation.Ready) == Admission.Busy, "no hidden queue");
        StationCommand?[] attempts = new StationCommand?[32];
        Parallel.For(0, attempts.Length, i => attempts[i] = s.TryTakeCommand());
        StationCommand move = attempts.Single(c => c is not null)!;
        Check(move.Key.Step == StepKind.Move && s.TryTakeCommand() is null, "32 concurrent takes transfer one command, no hidden resend");
        StationReply wrong = OfflineSimulation.Reply(move) with { Key = move.Key with { Session = Guid.NewGuid() } };
        Check(s.Receive(wrong) == ReplyDisposition.IgnoredCorrelation && s.Snapshot.Stage == StationStage.AwaitingMove, "foreign session ignored");
        Check(s.Receive(OfflineSimulation.Reply(move) with { Workpiece = "other" }) == ReplyDisposition.IgnoredCorrelation, "wrong workpiece");
        Check(s.Receive(OfflineSimulation.Reply(move) with { RecipeVersion = "v2" }) == ReplyDisposition.IgnoredCorrelation, "wrong recipe");
        Check(s.Receive(OfflineSimulation.Reply(move)) == ReplyDisposition.Advanced && s.Snapshot.Stage == StationStage.AwaitingCapture, "arrival then capture");
        Check(s.Receive(OfflineSimulation.Reply(move)) == ReplyDisposition.IgnoredCorrelation, "old step duplicate ignored");
        StationCommand capture = s.TryTakeCommand()!; StationReply captured = OfflineSimulation.Reply(capture);
        s.Receive(captured); StationCommand inspect = s.TryTakeCommand()!;
        Check(inspect.FrameId == captured.FrameId && inspect.Order == first, "capture identity passed to inspection");
        Check(s.Receive(OfflineSimulation.Reply(inspect, Verdict.Ng)) == ReplyDisposition.Advanced, "valid NG advances to publication");
        StationCommand publish = s.TryTakeCommand()!;
        Check(publish.Key.Step == StepKind.PublishResult && publish.Judgment == Verdict.Ng && publish.FrameId == captured.FrameId, "result same frame/workpiece/recipe");
        Check(s.Receive(OfflineSimulation.Reply(publish)) == ReplyDisposition.TerminalCreated && Terminal(s).Outcome == CycleOutcome.Completed, "matching NG ack completes cycle");
        Check(s.Receive(OfflineSimulation.Reply(publish)) == ReplyDisposition.NoPending, "duplicate ack no side effect");
        Check(!s.ReleaseCompletedCycle(OfflineSimulation.DeclaredRecovery(s)), "unconsumed terminal blocks next cycle");
        Check(!s.ConsumeTerminal(Terminal(s) with { }) && s.ConsumeTerminal(Terminal(s)) && !s.ConsumeTerminal(Terminal(s)), "original terminal consumed once");
        Check(!s.ReleaseCompletedCycle(OfflineSimulation.DeclaredRecovery(s) with { MaterialCleared = false }), "uncleared material blocks next cycle");
        Check(s.ReleaseCompletedCycle(OfflineSimulation.DeclaredRecovery(s)) && s.Snapshot.Stage == StationStage.Idle, "declare recovery before next cycle");
        Check(s.TryStart(first, OfflineSimulation.Ready) == Admission.Stale, "old request never repeats after completion");
        Check(s.TryStart(Order(s, 2), OfflineSimulation.Ready) == Admission.Accepted && s.Snapshot.Cycle == 2, "monotonic new cycle");

        TimeoutsAndRecovery();
        FailureAndClose();
        Console.WriteLine($"PASS {count} deterministic station state/identity/deadline/drain checks. No real devices or algorithms.");
    }
    private static void TimeoutsAndRecovery()
    {
        ManualStationClock clock = new(); StationController s = New(clock);
        s.TryStart(Order(s), OfflineSimulation.Ready); StationCommand move = s.TryTakeCommand()!;
        clock.ShiftWallClock(TimeSpan.FromDays(2)); s.Tick();
        Check(s.Snapshot.Stage == StationStage.AwaitingMove, "wall time does not expire monotonic deadline");
        clock.Advance(TimeSpan.FromSeconds(2)); s.Tick(); StationTerminal timed = Terminal(s);
        Check(timed.Reason == "TimedOut:Move" && s.Snapshot.Pending == move, "timeout retains taken obligation");
        Check(s.TryTakeCommand() is null, "timeout no new commands");
        s.ConsumeTerminal(timed);
        Check(!s.Reset(OfflineSimulation.DeclaredRecovery(s)), "even declared idle cannot bypass local pending");
        Check(s.Receive(OfflineSimulation.Reply(move)) == ReplyDisposition.DrainedLate && Terminal(s) == timed, "late move only drains, terminal frozen");
        Check(s.Snapshot.Pending is null && s.TryTakeCommand() is null, "late move does not trigger capture");
        Check(!s.Reset(OfflineSimulation.DeclaredRecovery(s) with { RecoveryVersion = 0 }), "old recovery epoch rejected");
        Check(!s.Reset(OfflineSimulation.DeclaredRecovery(s) with { ResultChannelReconciled = false }), "unknown result state blocks recovery");
        Check(s.Reset(OfflineSimulation.DeclaredRecovery(s)) && s.Snapshot.Order is null, "explicit logical reset, no retry");
        s.TryStart(Order(s, 2), OfflineSimulation.Ready); StationCommand next = s.TryTakeCommand()!;
        Check(s.Receive(OfflineSimulation.Reply(move)) == ReplyDisposition.IgnoredCorrelation && s.Snapshot.Pending == next, "late prior-cycle reply cannot satisfy next cycle");
        clock.Advance(TimeSpan.FromSeconds(2));
        Check(s.Receive(OfflineSimulation.Reply(next)) == ReplyDisposition.DrainedLate && s.Snapshot.Stage == StationStage.Faulted, "deadline checked on reply even without tick");
        ManualStationClock unsentClock = new(); StationController unsent = New(unsentClock);
        unsent.TryStart(Order(unsent), OfflineSimulation.Ready); unsentClock.Advance(TimeSpan.FromSeconds(2));
        Check(unsent.TryTakeCommand() is null && unsent.Snapshot.Pending is null && Terminal(unsent).Outcome == CycleOutcome.Faulted, "expired untaken command withdrawn before dispatch");
        StationController ack = New(clock = new()); ack.TryStart(Order(ack), OfflineSimulation.Ready);
        for (int i = 0; i < 3; i++) { StationCommand c = ack.TryTakeCommand()!; ack.Receive(OfflineSimulation.Reply(c)); }
        StationCommand result = ack.TryTakeCommand()!; clock.Advance(TimeSpan.FromSeconds(1)); ack.Tick();
        Check(Terminal(ack).Reason == "TimedOut:PublishResult", "ack timeout is not assumed delivery failure/success");
        Check(ack.Receive(OfflineSimulation.Reply(result)) == ReplyDisposition.DrainedLate && Terminal(ack).Outcome == CycleOutcome.Faulted, "late ack not retroactive normal success");
    }
    private static void FailureAndClose()
    {
        StationController s = New(); s.TryStart(Order(s), OfflineSimulation.Ready);
        StationCommand move = s.TryTakeCommand()!; s.Receive(OfflineSimulation.Reply(move, success: false));
        Check(Terminal(s).Reason == "StepFailed:Move" && s.Snapshot.Pending is null, "negative terminal reply known finished, fault stays");
        StationController frame = New(); frame.TryStart(Order(frame), OfflineSimulation.Ready);
        for (int i = 0; i < 2; i++) { StationCommand c = frame.TryTakeCommand()!; frame.Receive(OfflineSimulation.Reply(c)); }
        StationCommand inspection = frame.TryTakeCommand()!;
        Check(frame.Receive(OfflineSimulation.Reply(inspection) with { FrameId = Guid.NewGuid() }) == ReplyDisposition.RejectedPayload, "wrong frame latches fault");
        Check(frame.Snapshot.Pending == inspection && Terminal(frame).Reason == "ReplyPayloadMismatch", "wrong frame does not settle pending");
        Check(frame.Receive(OfflineSimulation.Reply(inspection)) == ReplyDisposition.DrainedLate && frame.TryTakeCommand() is null, "correct late inspection no result publication");
        StationController stopped = New(); stopped.TryStart(Order(stopped), OfflineSimulation.Ready);
        StationCommand running = stopped.TryTakeCommand()!; stopped.Stop();
        Check(stopped.Snapshot.Stage == StationStage.Stopped && stopped.Snapshot.Pending == running, "software stop does not prove physical stop");
        stopped.RequestClose();
        Check(stopped.Snapshot.Stage == StationStage.Closing && stopped.TryStart(Order(stopped, 2), OfflineSimulation.Ready) == Admission.Closed, "close blocks admission while draining");
        stopped.ConsumeTerminal(Terminal(stopped));
        Check(stopped.Snapshot.Stage == StationStage.Closing, "local consume cannot bypass running command");
        stopped.Receive(OfflineSimulation.Reply(running));
        Check(stopped.Snapshot.Stage == StationStage.Closed, "close after late drain and terminal consumption");
        Check(!stopped.Reset(OfflineSimulation.DeclaredRecovery(stopped)), "closed session cannot reset");
        StationController unsent = New(); unsent.TryStart(Order(unsent), OfflineSimulation.Ready); unsent.RequestClose();
        Check(unsent.Snapshot.Pending is null && unsent.Snapshot.Stage == StationStage.Closing, "close withdraws untransferred command");
        unsent.ConsumeTerminal(Terminal(unsent)); Check(unsent.Snapshot.Stage == StationStage.Closed, "close still needs terminal consumer");
        StationController idle = New(); idle.Stop();
        Check(idle.TryStart(Order(idle), OfflineSimulation.Ready) == Admission.Busy && idle.Reset(OfflineSimulation.DeclaredRecovery(idle)), "idle stop latched until explicit reset");
        idle.RequestClose(); Check(idle.Snapshot.Stage == StationStage.Closed, "idle close immediate");
        StationController bounded = New(); bounded.TryStart(Order(bounded), OfflineSimulation.Ready); StationCommand command = bounded.TryTakeCommand()!;
        for (int i = 0; i < 200; i++) bounded.Receive(OfflineSimulation.Reply(command) with { RecipeVersion = "foreign" });
        Check(bounded.Snapshot.Journal.Count == 128 && bounded.Snapshot.Journal[0].Sequence > 1, "journal bounded, not durable audit");
        Check(bounded.Snapshot.Journal.All(e => e.Order?.RecipeVersion == "recipe-v1"), "captured recipe preserved in trace");
    }
    private static void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException($"Station check failed: {name}"); count++; }
    private static void Reject(Action action, string name)
    { try { action(); } catch (ArgumentException) { count++; return; } throw new InvalidOperationException($"Expected rejection: {name}"); }
}
