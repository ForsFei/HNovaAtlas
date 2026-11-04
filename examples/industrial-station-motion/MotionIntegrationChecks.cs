using IndustrialMotion;
using IndustrialStation;

namespace IndustrialStationMotion;

internal static class MotionIntegrationChecks
{
    private static int count;
    private static string root = "";
    public static async Task RunAsync()
    {
        root = Path.Combine(Path.GetTempPath(), "hnova-station-motion-checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        await NormalAsync(); await IdentityAndFeedbackAsync(); await SettlingAsync(); await StopAsync(); await FaultAndReferenceAsync();
        await UnknownAsync(); await ZeroDistanceAsync(); await DeadlineAndCloseAsync();
        Console.WriteLine($"PASS {count} station/motion/reference/feedback/stop/unknown/close checks; files retained at {root}");
    }
    private static string DirectoryName() => Path.Combine(root, Guid.NewGuid().ToString("N"));
    private static StationCommand Command(string recipe = "demo-ok-v1") => new(new(Guid.NewGuid(), 1, 1, StepKind.Move), new(Guid.NewGuid(), 1, "part", recipe), null, null);
    private static async Task Prepare(MotionStationPort port)
    {
        port.OpenSimulation(); port.EnableSimulation(); Task<bool> homed = port.BeginReferenceSimulation();
        for (int i = 0; i < 1000 && !homed.IsCompleted; i++) port.AdvanceSimulation(.01);
        if (!await homed.WaitAsync(TimeSpan.FromSeconds(2))) throw new InvalidOperationException("Reference failed");
    }
    private static async Task Pump(MotionStationPort port, Func<bool> done)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        while (!done()) { port.AdvanceSimulation(.01); await Task.Delay(1, deadline.Token); }
    }
    private static async Task Close(MotionStationSession session, MotionStationPort port)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10)); session.RequestClose();
        while (!await session.CloseStepAsync())
        { if (!port.Snapshot.Disposed) port.AdvanceSimulation(.01); await Task.Delay(1, deadline.Token); }
    }
    private static async Task NormalAsync()
    {
        MotionStationPort port = new(); string dir = DirectoryName();
        MotionStationSession session = await MotionStationSession.OpenAsync(dir, port, new ManualStationClock());
        Check(!port.Snapshot.ReadyForNewCycle && port.Snapshot.HomeCalls == 0, "construction does not enable or home");
        Check(await Fails(() => session.StartAsync("no", "demo-ok-v1")) && session.Snapshot.Ledger.Cycles.Count == 0, "unprepared motion rejected before durable intent");
        session.OpenSimulation(); session.EnableSimulation(); Task<bool> reference = session.BeginReferenceSimulation();
        for (int i = 0; i < 1000 && !reference.IsCompleted; i++) port.AdvanceSimulation(.01);
        if (!await reference.WaitAsync(TimeSpan.FromSeconds(2))) throw new InvalidOperationException("Reference failed");
        Check(port.Snapshot.ReferenceQualified && port.Snapshot.HomeCalls == 1 && port.Snapshot.Enabled, "explicit connection enable and observed reference");
        Check(await session.StartAsync("part-ok", "demo-ok-v1") == Admission.Accepted, "prepared cycle accepted");
        Check(await Fails(() => session.BeginReferenceSimulation()), "cycle cannot initiate another home through session");
        await Pump(port, () => session.Snapshot.Host.Station.Stage == StationStage.Completed);
        Check(port.Snapshot.MoveCalls == 1 && port.Snapshot.HomeCalls == 1, "one move no automatic rehome");
        Check(Math.Abs(port.Snapshot.FeedbackPosition!.Value - 1) < .01 && session.Snapshot.Host.Station.Judgment == Verdict.Ok, "qualified model move then simulated OK inspection");
        Check(port.Snapshot.Observation == "Completed" && port.Snapshot.Reason.Contains("CompletionObserved"), "observer feedback completion recorded");
        await session.SaveResultAsync(); await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer);
        await session.StartAsync("part-ng", "demo-ng-v1"); await Pump(port, () => session.Snapshot.Host.Station.Stage == StationStage.Completed);
        Check(port.Snapshot.MoveCalls == 2 && Math.Abs(port.Snapshot.FeedbackPosition!.Value - 2) < .01 && session.Snapshot.Host.Station.Judgment == Verdict.Ng,
            "next captured position and NG recipe independent of first cycle");
        await session.SaveResultAsync(); await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer); await Close(session, port);
        Check(port.Snapshot.Disposed && session.Snapshot.Host.PortDisposed, "host owns model resource close");
        using StationLedger ledger = new(dir); Check(ledger.Report.Cycles.Count == 2 && ledger.Report.CanStartNewCycle, "both results saved and reviewed");
    }
    private static async Task IdentityAndFeedbackAsync()
    {
        await using MotionStationPort port = new(); await Prepare(port); StationCommand command = Command();
        Task<StationReply?> pending = port.ExecuteAsync(command);
        Check(!pending.IsCompleted && port.Snapshot.AxisResult == "Active", "old home completion never used for new move");
        Check(ReferenceEquals(pending, port.ExecuteAsync(command)) && port.Snapshot.MoveCalls == 1, "same operation returns original task no redispatch");
        Check(await Fails(() => port.ExecuteAsync(command with { Order = command.Order with { Workpiece = "other" } })), "same operation payload conflict");
        Check(await Fails(async () => await port.DisposeAsync()), "active model task prevents early disposal");
        for (int i = 0; i < 1000 && port.Snapshot.AxisResult != "Completed"; i++) port.AdvanceSimulation(.01);
        Check(port.Snapshot.AxisResult == "Completed" && !pending.IsCompleted, "model done still needs repeated stable observation");
        await Pump(port, () => pending.IsCompleted);
        StationReply? result = await pending;
        Check(result is { Success: true } && result.Key == command.Key && result.Workpiece == command.Order.Workpiece, "reply retains original station identity");
        Check(await port.ObserveAsync(command) == result && port.Snapshot.MoveCalls == 1, "read-only observe does not move or tick");
        Check(await port.ObserveAsync(command with { Order = command.Order with { RecipeVersion = "demo-ng-v1" } }) is null, "query rejects changed captured recipe");
        Check(await Fails(() => port.ExecuteAsync(command with { Key = command.Key with { Cycle = 1, Operation = 0 } })), "invalid old operation rejected");
        await using MotionStationPort noFeedback = new(new AxisSettings(HasFeedback: false)); await Prepare(noFeedback);
        Task<StationReply?> unqualified = noFeedback.ExecuteAsync(Command()); await Pump(noFeedback, () => unqualified.IsCompleted);
        Check(await unqualified is null && !noFeedback.Snapshot.ReferenceQualified, "missing feedback cannot qualify move, no command-only fallback");
    }
    private static async Task SettlingAsync()
    {
        await using MotionStationPort port = new(); await Prepare(port); Task<StationReply?> pending = port.ExecuteAsync(Command());
        port.SetInputsForChecks(new(InPosition: false)); port.SetFollowingOffsetForChecks(.02);
        for (int i = 0; i < 100; i++) port.AdvanceSimulation(.01);
        Check(!pending.IsCompleted && port.Snapshot.Busy && Math.Abs(port.Snapshot.CommandPosition - 1) < .01,
            "command at target cannot substitute for in-position and feedback settling");
        port.SetFollowingOffsetForChecks(0); port.SetInputsForChecks(new()); await Pump(port, () => pending.IsCompleted);
        Check(await pending is { Success: true }, "fresh feedback/in-position and repeated observations qualify result");
    }
    private static async Task StopAsync()
    {
        MotionStationPort port = new(); await Prepare(port);
        MotionStationSession session = await MotionStationSession.OpenAsync(DirectoryName(), port, new ManualStationClock());
        await session.StartAsync("stopping", "demo-ng-v1"); await Pump(port, () => port.Snapshot.MoveCalls == 1);
        port.AdvanceSimulation(.03); await session.StopFlowAsync();
        Check(port.Snapshot.Busy && port.Snapshot.StopCalls == 0, "flow stop alone sends no axis stop");
        Check(session.Snapshot.Host.Station.Terminal!.Outcome == CycleOutcome.Stopped && session.Snapshot.Host.Station.Pending is not null,
            "flow stop retains pending motion");
        port.RequestOrdinaryStopSimulation(); await Pump(port, () => !session.Snapshot.Host.TaskActive && session.Snapshot.Host.Station.Pending is null);
        Check(port.Snapshot.StopCalls == 1 && port.Snapshot.AxisResult == "Stopped", "ordinary model stop observed separately");
        Check(session.Snapshot.Host.Station.Terminal!.Outcome == CycleOutcome.Stopped && session.Snapshot.Host.Station.FrameId is null,
            "stopped is never target complete or capture trigger");
        await session.SaveResultAsync(); await Close(session, port);
    }
    private static async Task FaultAndReferenceAsync()
    {
        MotionStationPort port = new(); await Prepare(port);
        MotionStationSession session = await MotionStationSession.OpenAsync(DirectoryName(), port, new ManualStationClock());
        await session.StartAsync("fault", "demo-ok-v1"); await Pump(port, () => port.Snapshot.MoveCalls == 1); port.AdvanceSimulation(.03);
        port.SetInputsForChecks(new(Alarm: true)); port.AdvanceSimulation(.01);
        await Pump(port, () => !session.Snapshot.Host.TaskActive && session.Snapshot.Host.Station.Terminal is not null);
        Check(session.Snapshot.Host.Station.Terminal!.Reason == "StepFailed:Move" && session.Snapshot.Host.Station.Pending is null,
            "known terminated model fault maps settled failure");
        Check(!port.Snapshot.Enabled && !port.Snapshot.Homed && !port.Snapshot.ReferenceQualified, "fault revokes enable home and reference");
        port.SetInputsForChecks(new()); port.ResetSimulationFault();
        Check(port.Snapshot.AxisState == "Disabled" && !port.Snapshot.Enabled && !port.Snapshot.Homed && port.Snapshot.MoveCalls == 1,
            "explicit fault reset does not enable home or replay");
        await session.SaveResultAsync(); await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.ConfirmedNotPublished);
        Check(await Fails(() => session.StartAsync("still-not-ready", "demo-ok-v1")), "local idle cannot bypass lost reference");
        await Close(session, port);
    }
    private static async Task UnknownAsync()
    {
        await using (MotionStationPort port = new())
        {
            await Prepare(port); StationCommand command = Command("demo-ng-v1"); Task<StationReply?> pending = port.ExecuteAsync(command);
            port.AdvanceSimulation(.02); port.LoseObservationForChecks(.3);
            Check(await pending is null && port.Snapshot.Observation == "Unknown", "observation gap ends task unknown without fake terminal");
            Check(port.Snapshot.Busy && port.Snapshot.StopCalls == 0, "unknown observation does not stop simulated motion");
            Check(await Fails(async () => await port.DisposeAsync()), "completed task does not permit disposing busy axis");
            port.RequestOrdinaryStopSimulation(); await Pump(port, () => !port.Snapshot.Busy);
            Check(await port.ObserveAsync(command) is null && port.Snapshot.MoveCalls == 1, "later stopped telemetry never rewrites unknown move outcome");
        }
        await using (MotionStationPort port = new())
        {
            await Prepare(port); StationCommand command = Command(); Task<StationReply?> pending = port.ExecuteAsync(command);
            port.SetInputsForChecks(new(BusOperational: false)); port.AdvanceSimulation(.01);
            Check(await pending is null && !port.Snapshot.DataValid && !port.Snapshot.ReferenceQualified, "bus loss remains unknown, no stopped proof");
            Check(await port.ObserveAsync(command) is null, "invalid data cannot reconcile pending operation");
        }
    }
    private static async Task ZeroDistanceAsync()
    {
        await using MotionStationPort port = new(); await Prepare(port); StationCommand command = Command();
        Task<StationReply?> first = port.ExecuteAsync(command); await Pump(port, () => first.IsCompleted); await first;
        Task<StationReply?> zero = port.ExecuteAsync(command with { Key = command.Key with { Cycle = 2, Operation = 5 }, Order = command.Order with { Number = 2 } });
        await Pump(port, () => zero.IsCompleted);
        Check(await zero is null && port.Snapshot.Observation == "Unknown" && port.Snapshot.Reason.StartsWith("StartNotObserved"),
            "same target without observed motion not silently accepted by search-style policy");
        Check(port.Snapshot.MoveCalls == 2 && !port.Snapshot.Busy, "zero-distance unknown does not fabricate third dispatch");
    }
    private static async Task DeadlineAndCloseAsync()
    {
        MotionStationPort port = new(); await Prepare(port); ManualStationClock clock = new();
        MotionStationSession session = await MotionStationSession.OpenAsync(DirectoryName(), port, clock);
        await session.StartAsync("late", "demo-ng-v1"); await Pump(port, () => port.Snapshot.MoveCalls == 1);
        double position = port.Snapshot.CommandPosition; clock.Advance(TimeSpan.FromSeconds(2));
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        while (session.Snapshot.Host.Station.Stage != StationStage.Faulted) await Task.Delay(1, deadline.Token);
        Check(session.Snapshot.Host.TaskActive && port.Snapshot.Busy && port.Snapshot.CommandPosition == position,
            "host monotonic timeout does not advance or stop model clock");
        session.RequestClose(); while (session.Snapshot.Host.Station.Stage != StationStage.Closing) await Task.Delay(1, deadline.Token);
        Check(!await session.CloseStepAsync() && !port.Snapshot.Disposed && port.Snapshot.StopCalls == 0, "close retains active axis task without implicit stop");
        await Close(session, port);
        Check(session.Snapshot.Host.Station.Terminal!.Outcome == CycleOutcome.Faulted && session.Snapshot.Host.Station.FrameId is null && port.Snapshot.MoveCalls == 1,
            "late qualified move drains only, fault unchanged and no capture");
        Check(session.Snapshot.Closed && port.Snapshot.Disposed && !session.Snapshot.Ledger.CanStartNewCycle, "close releases resources without automatic review");
    }
    private static async Task<bool> Fails(Func<Task> work)
    { try { await work(); return false; } catch (Exception error) when (error is InvalidOperationException or ArgumentException) { return true; } }
    private static void Check(bool value, string label) { if (!value) throw new InvalidOperationException("Motion integration check failed: " + label); count++; }
    public static async Task DemoAsync()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hnova-station-motion-" + Guid.NewGuid().ToString("N"));
        MotionStationPort port = new();
        MotionStationSession session = await MotionStationSession.OpenAsync(dir, port, new ManualStationClock());
        session.OpenSimulation(); session.EnableSimulation(); Task<bool> referenced = session.BeginReferenceSimulation();
        await Pump(port, () => referenced.IsCompleted);
        if (!await referenced) throw new InvalidOperationException("Reference failed");
        Console.WriteLine("IN-MEMORY AXIS ONLY: no native SDK/network/card/servo. Files retained at " + dir);
        Console.WriteLine($"Explicit reference: qualified={port.Snapshot.ReferenceQualified}; homeCalls={port.Snapshot.HomeCalls}");
        await session.StartAsync("motion-ng", "demo-ng-v1"); await Pump(port, () => session.Snapshot.Host.Station.Stage == StationStage.Completed);
        Console.WriteLine($"Qualified move: feedback={port.Snapshot.FeedbackPosition:F3} mm; moves={port.Snapshot.MoveCalls}; judgment={session.Snapshot.Host.Station.Judgment}");
        await session.SaveResultAsync(); await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer); await Close(session, port);
        Console.WriteLine($"Saved/reviewed/closed={session.Snapshot.Closed}; modelDisposed={port.Snapshot.Disposed}");
    }
}
