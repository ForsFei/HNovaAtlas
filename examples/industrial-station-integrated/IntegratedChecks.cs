using IndustrialStation;
using IndustrialStationVision;

namespace IndustrialStationIntegrated;

internal static class IntegratedChecks
{
    private static int count;
    private static string root = "";
    public static async Task RunAsync()
    {
        root = Path.Combine(Path.GetTempPath(), "hnova-station-integrated-checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        await NormalAsync(); await DirectIdentityAsync(); await HandoffValidationAsync(); await StopAndRecoverAsync();
        await LateMoveAsync(); await LateCaptureAsync(); await LateInspectionAsync(); await RestartAsync();
        Console.WriteLine($"PASS {count} integrated motion/vision/order/stop/late-close/restart checks; files retained at {root}");
    }
    private static string DirectoryName() => Path.Combine(root, Guid.NewGuid().ToString("N"));
    private static async Task Prepare(IntegratedStationSession session, IntegratedStationPort port)
    {
        session.OpenMotionSimulation(); session.EnableMotionSimulation(); session.OpenReplay(); Task<bool> reference = session.BeginReferenceSimulation();
        for (int i = 0; i < 1000 && !reference.IsCompleted; i++) port.AdvanceSimulation(.01);
        if (!await reference.WaitAsync(TimeSpan.FromSeconds(2))) throw new InvalidOperationException("Reference failed");
    }
    private static async Task Pump(IntegratedStationPort port, Func<bool> done)
    { using CancellationTokenSource limit = new(TimeSpan.FromSeconds(10)); while (!done()) { port.AdvanceSimulation(.01); await Task.Delay(1, limit.Token); } }
    private static async Task Until(IntegratedStationSession session, Func<ApplicationSnapshot, bool> done)
    { using CancellationTokenSource limit = new(TimeSpan.FromSeconds(10)); while (!done(session.Snapshot)) await Task.Delay(1, limit.Token); }
    private static async Task Close(IntegratedStationSession session, IntegratedStationPort port)
    { using CancellationTokenSource limit = new(TimeSpan.FromSeconds(10)); session.RequestClose(); while (!await session.CloseStepAsync()) { port.TryAdvanceSimulation(.01); await Task.Delay(1, limit.Token); } }
    private static async Task NormalAsync()
    {
        string directory = DirectoryName(); IntegratedStationPort port = new(); IntegratedStationSession session = await IntegratedStationSession.OpenAsync(directory, port, new ManualStationClock());
        Check(await Fails(() => session.StartAsync("no", "demo-ok-v1")) && session.Snapshot.Ledger.Cycles.Count == 0, "unprepared pair rejected before intent");
        await Prepare(session, port);
        Check(session.Devices.Motion.ReferenceQualified && session.Devices.Vision.Open, "both explicit preparation domains ready");
        Guid previousFrame = Guid.Empty;
        for (int i = 1; i <= 2; i++)
        {
            string recipe = i == 1 ? "demo-ok-v1" : "demo-ng-v1";
            await session.StartAsync("part-" + i, recipe);
            await Pump(port, () => port.Snapshot.Motion.MoveCalls == i);
            Check(session.Devices.Vision.FramesAcquired == (ulong)(i - 1) && !session.Devices.MoveHandedToVision, "no frame before qualified move");
            Check(await Fails(() => session.BeginReferenceSimulation()), "cannot rehome admitted workpiece");
            await Pump(port, () => session.Snapshot.Host.Station.Stage == StationStage.Completed);
            IntegratedPortSnapshot d = session.Devices;
            Check(d.Motion.MoveCalls == i && d.Vision.SimulatedMoveCalls == 0 && d.Vision.ExternalMoveHandoffs == i, "one axis move and one data handoff, no second fake move");
            Check(d.Vision.FramesAcquired == (ulong)i && d.Vision.MeasureCalls == i && d.Vision.PublishCalls == i && d.Vision.OutstandingLeases == 0,
                "one original frame, actual measurement, simulated publication and returned lease");
            Check(d.Motion.FeedbackPosition == i && d.Vision.Measurement!.Mean == (i == 1 ? 220 : 75) &&
                session.Snapshot.Host.Station.Judgment == (i == 1 ? Verdict.Ok : Verdict.Ng), "captured target and actual ROI verdict match recipe/data");
            Check(session.Snapshot.Host.Station.FrameId == d.Vision.FrameId && d.Vision.FrameId != previousFrame && d.MoveOperation!.Cycle == session.Snapshot.Host.Station.Cycle,
                "same station cycle links move and original frame");
            previousFrame = d.Vision.FrameId!.Value;
            Check(await Fails(() => session.AcknowledgeVisionAfterSavedAsync()), "cannot acknowledge original processing result before local save");
            await session.SaveResultAsync();
            Check(await Fails(() => session.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer)), "review waits vision receipt");
            Check(await session.AcknowledgeVisionAfterSavedAsync() && session.Devices.Vision.RetainedResults == 0, "save/consume precedes processing acknowledgment");
            await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer);
        }
        await Close(session, port);
        Check(session.Devices.Disposed && session.Devices.Motion.Disposed && session.Devices.Vision.Disposed && session.Snapshot.Host.PortDisposed, "all owned modules closed");
        Check(!port.TryAdvanceSimulation(.01), "close pump no longer ticks disposed child modules");
        using StationLedger reopened = new(directory);
        Check(reopened.Report.Cycles.Count == 2 && reopened.Report.CanStartNewCycle && reopened.Report.Cycles[1].Terminal!.Judgment == Verdict.Ng,
            "both end-to-end outcomes survive reopen");
    }
    private static StationCommand Command() => new(new(Guid.NewGuid(), 1, 1, StepKind.Move), new(Guid.NewGuid(), 1, "direct", "demo-ok-v1"), null, null);
    private static async Task DirectIdentityAsync()
    {
        await using IntegratedStationPort port = new(); port.OpenMotionSimulation(); port.EnableMotionSimulation(); port.OpenReplay(); Task<bool> reference = port.BeginReferenceSimulation();
        for (int i = 0; i < 1000 && !reference.IsCompleted; i++) port.AdvanceSimulation(.01); await reference;
        StationCommand command = Command(); Task<StationReply?> moving = port.ExecuteAsync(command);
        Check(ReferenceEquals(moving, port.ExecuteAsync(command)) && port.Snapshot.Motion.MoveCalls == 1, "duplicate composite move shares original task");
        Check(await Fails(() => port.ExecuteAsync(command with { Order = command.Order with { Workpiece = "conflict" } })), "move payload conflict never dispatches");
        StationCommand capture = command with { Key = command.Key with { Step = StepKind.Capture, Operation = 2 } };
        Check(await Fails(() => port.ExecuteAsync(capture)) && port.Snapshot.Vision.FramesAcquired == 0, "capture rejected before handoff");
        await Pump(port, () => moving.IsCompleted); StationReply result = (await moving)!;
        Check(await port.ObserveAsync(command) == result && port.Snapshot.Motion.MoveCalls == 1 && port.Snapshot.Vision.ExternalMoveHandoffs == 1,
            "readonly composite observe dispatches nothing");
        await port.ExecuteAsync(capture);
        Check(await Fails(() => port.ExecuteAsync(command with { Key = command.Key with { Step = StepKind.Inspect, Operation = 3 }, FrameId = Guid.NewGuid() })), "wrong frame refused by original visual adapter");
    }
    private static async Task HandoffValidationAsync()
    {
        await using VisionStationPort vision = new(); vision.OpenReplay(); StationCommand command = Command();
        StationReply good = new(command.Key, command.Order.Workpiece, command.Order.RecipeVersion, true, null, null);
        foreach (StationReply bad in new[] { good with { Success = false }, good with { Key = good.Key with { Cycle = 2 } }, good with { Workpiece = "wrong" }, good with { FrameId = Guid.NewGuid() } })
            Check(await Fails(() => { vision.RecordQualifiedExternalMove(command, bad); return Task.CompletedTask; }), "external data ingress refuses mismatched or failed evidence");
        Check(vision.RecordQualifiedExternalMove(command, good) && !vision.RecordQualifiedExternalMove(command, good), "external move handoff structural idempotence");
        Check(vision.Snapshot.SimulatedMoveCalls == 0 && vision.Snapshot.ExternalMoveHandoffs == 1 && vision.Snapshot.FramesAcquired == 0,
            "recording evidence is neither a motion dispatch nor a capture");
    }
    private static async Task StopAndRecoverAsync()
    {
        IntegratedStationPort port = new(); IntegratedStationSession session = await IntegratedStationSession.OpenAsync(DirectoryName(), port, new ManualStationClock()); await Prepare(session, port);
        await session.StartAsync("stop", "demo-ok-v1"); await Pump(port, () => session.Devices.Motion.MoveCalls == 1); port.AdvanceSimulation(.03);
        await session.StopFlowAsync();
        Check(session.Devices.Motion.Busy && session.Devices.Motion.StopCalls == 0 && session.Devices.Vision.FramesAcquired == 0, "software flow stop does not stop axis or trigger capture");
        port.RequestOrdinaryStopSimulation(); await Pump(port, () => !session.Snapshot.Host.TaskActive && session.Snapshot.Host.Station.Pending is null);
        Check(!session.Devices.MoveHandedToVision && session.Devices.Vision.ExternalMoveHandoffs == 0 && session.Snapshot.Host.Station.Terminal!.Outcome == CycleOutcome.Stopped,
            "stopped motion cannot qualify visual handoff");
        await session.SaveResultAsync(); Check(!await session.AcknowledgeVisionAfterSavedAsync(), "failed move creates no fictional vision receipt");
        await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.ConfirmedNotPublished);
        await session.StartAsync("after-stop", "demo-ng-v1"); await Pump(port, () => session.Snapshot.Host.Station.Stage == StationStage.Completed);
        Check(session.Devices.Motion.MoveCalls == 2 && session.Devices.Vision.FramesAcquired == 1 && session.Devices.Vision.SimulatedMoveCalls == 0,
            "explicit review permits one fresh cycle, no replay of stopped move");
        await session.SaveResultAsync(); await session.AcknowledgeVisionAfterSavedAsync(); await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer); await Close(session, port);
    }
    private static async Task LateMoveAsync()
    {
        IntegratedStationPort port = new(); ManualStationClock clock = new(); IntegratedStationSession session = await IntegratedStationSession.OpenAsync(DirectoryName(), port, clock); await Prepare(session, port);
        await session.StartAsync("late-move", "demo-ng-v1"); await Pump(port, () => session.Devices.Motion.MoveCalls == 1);
        clock.Advance(TimeSpan.FromSeconds(2)); await Until(session, s => s.Host.Station.Stage == StationStage.Faulted);
        session.RequestClose(); await Until(session, s => s.Host.Station.Stage == StationStage.Closing);
        Check(!await session.CloseStepAsync() && session.Devices.TaskActive && session.Devices.Motion.Busy, "closing waits actual routed motion task");
        await Close(session, port);
        Check(session.Devices.MoveHandedToVision && session.Devices.Vision.FramesAcquired == 0 && session.Snapshot.Host.Station.FrameId is null,
            "late data handoff alone cannot trigger Capture after original timeout");
        Check(session.Snapshot.Host.Station.Terminal!.Outcome == CycleOutcome.Faulted && session.Devices.Motion.MoveCalls == 1, "late qualified move never changes original fault or redispatches");
    }
    private static async Task LateCaptureAsync()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously), release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IntegratedStationPort port = new(step => { if (step != StepKind.Capture) return Task.CompletedTask; entered.TrySetResult(); return release.Task; });
        ManualStationClock clock = new(); IntegratedStationSession session = await IntegratedStationSession.OpenAsync(DirectoryName(), port, clock); await Prepare(session, port);
        await session.StartAsync("late-capture", "demo-ok-v1"); await Pump(port, () => entered.Task.IsCompleted);
        Check(session.Devices.Motion.MoveCalls == 1 && session.Devices.Vision.FramesAcquired == 1 && session.Devices.Vision.OutstandingLeases == 0, "captured owned pixels follow one qualified move");
        clock.Advance(TimeSpan.FromSeconds(1)); await Until(session, s => s.Host.Station.Stage == StationStage.Faulted);
        session.RequestClose(); await Until(session, s => s.Host.Station.Stage == StationStage.Closing); await session.CloseStepAsync();
        release.TrySetResult(); await Close(session, port);
        Check(session.Devices.Vision.ProcessingSubmissions == 0 && session.Snapshot.Host.Station.FrameId is null && session.Devices.Vision.PublishCalls == 0,
            "late frame never becomes accepted station image or starts inspection");
    }
    private static async Task LateInspectionAsync()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously), release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IntegratedStationPort port = new(step => { if (step != StepKind.Inspect) return Task.CompletedTask; entered.TrySetResult(); return release.Task; });
        ManualStationClock clock = new(); IntegratedStationSession session = await IntegratedStationSession.OpenAsync(DirectoryName(), port, clock); await Prepare(session, port);
        await session.StartAsync("late-result", "demo-ok-v1"); await Pump(port, () => entered.Task.IsCompleted);
        Check(await Fails(async () => await port.DisposeAsync()) && session.Devices.TaskActive, "completed inner algorithm does not mean composite reply has ended");
        clock.Advance(TimeSpan.FromSeconds(3)); await Until(session, s => s.Host.Station.Stage == StationStage.Faulted);
        session.RequestClose(); await Until(session, s => s.Host.Station.Stage == StationStage.Closing); await session.CloseStepAsync();
        release.TrySetResult(); await Close(session, port);
        Check(session.Devices.Vision.Judgment == Verdict.Ok && session.Snapshot.Host.Station.Judgment is null && session.Devices.Vision.PublishCalls == 0,
            "late diagnostic OK cannot become accepted verdict or publication");
        Check(session.Devices.Motion.Disposed && session.Devices.Vision.Disposed && session.Devices.Vision.RetainedResults == 1,
            "close drains both owners without fake processing acknowledgment");
    }
    private static async Task RestartAsync()
    {
        string directory = DirectoryName(); Guid old = Guid.NewGuid();
        using (StationLedger ledger = new(directory)) ledger.Begin(old, 1, new(StationApplication.DemoProducer, 6, "old", "demo-ok-v1"));
        IntegratedStationPort port = new(); IntegratedStationSession session = await IntegratedStationSession.OpenAsync(directory, port, new ManualStationClock());
        Check(await Fails(() => { session.OpenMotionSimulation(); return Task.CompletedTask; }) && await Fails(() => { session.OpenReplay(); return Task.CompletedTask; }),
            "old unreviewed intent blocks both preparation domains");
        await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.ConfirmedNotPublished);
        Check(session.Snapshot.Ledger.Cycles[0].Terminal is null && !session.Devices.Motion.Enabled && !session.Devices.Vision.Open,
            "review old intent restores no task, image, enable or home");
        await Prepare(session, port); await session.StartAsync("new", "demo-ok-v1"); await Pump(port, () => session.Snapshot.Host.Station.Stage == StationStage.Completed);
        Check(session.Snapshot.Host.Station.Session != old && session.Snapshot.Host.Station.Order!.Number == 7 && session.Devices.Motion.MoveCalls == 1,
            "new reviewed cycle gets fresh session and persistent producer high water");
        await Close(session, port);
    }
    private static async Task<bool> Fails(Func<Task> work) { try { await work(); return false; } catch (Exception error) when (error is InvalidOperationException or ArgumentException) { return true; } }
    private static void Check(bool value, string label) { if (!value) throw new InvalidOperationException("Integrated check failed: " + label); count++; }
    public static async Task DemoAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "hnova-station-integrated-" + Guid.NewGuid().ToString("N"));
        IntegratedStationPort port = new(); IntegratedStationSession session = await IntegratedStationSession.OpenAsync(directory, port, new ManualStationClock()); await Prepare(session, port);
        Console.WriteLine("OFFLINE END-TO-END ONLY: no real card/camera/native SDK/network. Files retained at " + directory);
        for (int i = 1; i <= 2; i++)
        {
            string recipe = i == 1 ? "demo-ok-v1" : "demo-ng-v1"; await session.StartAsync("part-" + i, recipe);
            await Pump(port, () => session.Snapshot.Host.Station.Stage == StationStage.Completed);
            Console.WriteLine($"Cycle {i}: feedback={session.Devices.Motion.FeedbackPosition:F3}mm; frame={session.Devices.Vision.FrameStamp!.Value.Number}; mean={session.Devices.Vision.Measurement!.Mean:F1}; verdict={session.Snapshot.Host.Station.Judgment}");
            await session.SaveResultAsync(); await session.AcknowledgeVisionAfterSavedAsync(); await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer);
        }
        await Close(session, port);
        Console.WriteLine($"moves={session.Devices.Motion.MoveCalls}; visionFakeMoves={session.Devices.Vision.SimulatedMoveCalls}; handoffs={session.Devices.Vision.ExternalMoveHandoffs}; closed={session.Snapshot.Closed}");
    }
}
