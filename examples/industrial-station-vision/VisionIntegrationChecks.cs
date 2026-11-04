using IndustrialStation;
using IndustrialVision;

namespace IndustrialStationVision;

internal static class VisionIntegrationChecks
{
    private static int count;
    private static string root = "";
    public static async Task RunAsync()
    {
        root = Path.Combine(Path.GetTempPath(), "hnova-station-vision-checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        await NormalAsync(); await RestartAsync(); await IdentityAsync(); await InvalidFramesAsync(); await ProcessingFailureAsync(false); await ProcessingFailureAsync(true);
        await LateProcessingAsync(); await LateCaptureAsync();
        Console.WriteLine($"PASS {count} station/replay/frame/ROI/receipt/late-close checks; files retained at {root}");
    }
    private static string DirectoryName() => Path.Combine(root, Guid.NewGuid().ToString("N"));
    private static async Task Until(VisionStationSession session, Func<ApplicationSnapshot, bool> condition)
    { using CancellationTokenSource limit = new(TimeSpan.FromSeconds(10)); while (!condition(session.Snapshot)) await Task.Delay(2, limit.Token); }
    private static async Task Close(VisionStationSession session)
    { using CancellationTokenSource limit = new(TimeSpan.FromSeconds(10)); session.RequestClose(); while (!await session.CloseStepAsync()) await Task.Delay(2, limit.Token); }
    private static async Task NormalAsync()
    {
        string directory = DirectoryName(); VisionStationPort port = new();
        VisionStationSession session = await VisionStationSession.OpenAsync(directory, port, new ManualStationClock());
        Check(await Fails(() => session.StartAsync("closed", "demo-ok-v1")) && session.Snapshot.Ledger.Cycles.Count == 0, "closed replay rejects before durable intent");
        session.OpenReplay(); Check(session.Vision.Open && session.Vision.ReadyForNextCycle, "explicit replay open");
        await session.StartAsync("first", "demo-ok-v1"); await Until(session, s => s.Host.Station.Stage == StationStage.Completed);
        Guid frame = session.Vision.FrameId!.Value; FrameStamp stamp = session.Vision.FrameStamp!.Value;
        Check(session.Vision.Measurement is { Mean: 220, Count: 4, AtOrAboveThreshold: 4 } && session.Snapshot.Host.Station.Judgment == Verdict.Ok,
            "actual original ROI pixels determine OK, padded bytes excluded");
        Check(session.Snapshot.Host.Station.FrameId == frame && stamp.Number == 1 && session.Vision.FramesAcquired == 1, "local frame GUID bound to one source stamp");
        Check(session.Vision.OutstandingLeases == 0 && session.Vision.RetainedResults == 1, "borrow returned before retained result acknowledgment");
        Check(await Fails(() => session.AcknowledgeVisionAfterSavedAsync()), "cannot ack worker before local terminal saved/consumed");
        await session.SaveResultAsync();
        Check(await Fails(() => session.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer)), "local release waits vision receipt acknowledgment");
        Check(await session.AcknowledgeVisionAfterSavedAsync() && session.Vision.RetainedResults == 0 && session.Vision.CycleAcknowledged, "saved local receipt allows original pipeline ack");
        Check(await session.AcknowledgeVisionAfterSavedAsync() && session.Vision.RetainedResults == 0, "repeated ack idempotent");
        await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer);
        await session.StartAsync("second", "demo-ok-v1"); await Until(session, s => s.Host.Station.Stage == StationStage.Completed);
        Check(session.Vision.Measurement is { Mean: 35, AtOrAboveThreshold: 0 } && session.Snapshot.Host.Station.Judgment == Verdict.Ng,
            "same recipe on inverse pixels yields NG, not hardcoded recipe verdict");
        Check(session.Vision.FrameId != frame && session.Vision.FrameStamp!.Value.Session == stamp.Session && session.Vision.FrameStamp.Value.Number == 2,
            "new cycle gets new frame identity within same replay session");
        Check(session.Vision.ProcessingSubmissions == 2 && session.Vision.MeasureCalls == 2 && session.Vision.PublishCalls == 2, "one capture/process/publish per cycle");
        await session.SaveResultAsync(); await session.AcknowledgeVisionAfterSavedAsync(); await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer);
        await Close(session); Check(session.Vision.Disposed && !session.Vision.Open && session.Snapshot.Host.PortDisposed, "worker and replay closed through host ownership");
        using StationLedger reopened = new(directory);
        Check(reopened.Report.Cycles[1].Terminal!.FrameId == session.Vision.FrameId && reopened.Report.Cycles[1].Terminal!.Judgment == Verdict.Ng,
            "matching station frame/verdict survived file reopen");
    }
    private static async Task RestartAsync()
    {
        string directory = DirectoryName(); Guid oldSession = Guid.NewGuid();
        using (StationLedger ledger = new(directory)) ledger.Begin(oldSession, 1, new(StationApplication.DemoProducer, 7, "interrupted", "demo-ok-v1"));
        VisionStationPort port = new(); VisionStationSession session = await VisionStationSession.OpenAsync(directory, port, new ManualStationClock());
        Check(await Fails(() => { session.OpenReplay(); return Task.CompletedTask; }) && !session.Vision.Open, "restart review gate does not open source");
        await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.ConfirmedNotPublished);
        Check(session.Snapshot.Ledger.Cycles[0].Terminal is null && session.Snapshot.Ledger.Cycles[0].Review is not null && !session.Vision.Open,
            "old unknown cycle can be reviewed without opening replay or inventing terminal");
        session.OpenReplay(); await session.StartAsync("after-review", "demo-ok-v1"); await Until(session, s => s.Host.Station.Stage == StationStage.Completed);
        Check(session.Snapshot.Host.Station.Order!.Number == 8 && session.Snapshot.Host.Station.Session != oldSession && session.Vision.FrameStamp!.Value.Number == 1,
            "reviewed restart uses new host/frame session and durable source high water");
        await Close(session);
        Check(!session.Snapshot.Ledger.CanStartNewCycle && session.Snapshot.Closed, "close does not auto-review new cycle");
    }
    private static StationCommand Command() => new(new(Guid.NewGuid(), 1, 1, StepKind.Move), new(Guid.NewGuid(), 1, "direct", "demo-ok-v1"), null, null);
    private static StationCommand Next(StationCommand first, StepKind step, Guid? frame = null, Verdict? verdict = null) =>
        first with { Key = first.Key with { Operation = (long)step + 1, Step = step }, FrameId = frame, Judgment = verdict };
    private static async Task IdentityAsync()
    {
        await using VisionStationPort port = new(); port.OpenReplay(); StationCommand move = Command(); await port.ExecuteAsync(move);
        StationCommand capture = Next(move, StepKind.Capture); Task<StationReply?> frameTask = port.ExecuteAsync(capture); StationReply frame = (await frameTask)!;
        Check(ReferenceEquals(frameTask, port.ExecuteAsync(capture)) && port.Snapshot.FramesAcquired == 1, "duplicate capture reuses original task no second frame");
        Check(await Fails(() => port.ExecuteAsync(capture with { Order = capture.Order with { Workpiece = "other" } })), "capture key different workpiece rejected");
        Check(await Fails(() => port.ExecuteAsync(Next(move, StepKind.Inspect, Guid.NewGuid())) ) && port.Snapshot.ProcessingSubmissions == 0, "wrong frame never enters pipeline");
        StationCommand inspect = Next(move, StepKind.Inspect, frame.FrameId); Task<StationReply?> processed = port.ExecuteAsync(inspect); StationReply result = (await processed)!;
        Check(ReferenceEquals(processed, port.ExecuteAsync(inspect)) && port.Snapshot.MeasureCalls == 1, "duplicate inspection no reprocess");
        Check(await port.ObserveAsync(inspect) == result && port.Snapshot.FramesAcquired == 1 && port.Snapshot.MeasureCalls == 1, "readonly observe no acquisition or processing");
        Check(await port.ObserveAsync(inspect with { FrameId = Guid.NewGuid() }) is null, "readonly rejects unrelated frame");
        Check(await Fails(() => port.ExecuteAsync(Next(move, StepKind.PublishResult, frame.FrameId, Verdict.Ng))) && port.Snapshot.PublishCalls == 0,
            "changed verdict never receives publish acknowledgment");
        await port.ExecuteAsync(Next(move, StepKind.PublishResult, frame.FrameId, result.Judgment));
        Check(await Fails(() => port.ExecuteAsync(move with { Key = move.Key with { Cycle = 2, Operation = 5 }, Order = move.Order with { Number = 2 } })),
            "unacknowledged cycle prevents next frame overwriting owned pixels");
    }
    private static async Task InvalidFramesAsync()
    {
        Func<CaptureFrame, CaptureFrame>[] changes = [p => p with { CameraEpoch = Guid.NewGuid() }, p => p with { TriggerSequence = p.TriggerSequence + 1 },
            p => p with { Complete = false }, p => p with { IdentityQualified = false }, p => p with { Frame = p.Frame with { Stamp = new(Guid.NewGuid(), 1) } }];
        foreach (Func<CaptureFrame, CaptureFrame> change in changes)
        {
            await using VisionStationPort port = new(PlainPgm.LoadFixture(), transform: change); port.OpenReplay(); StationCommand command = Command(); await port.ExecuteAsync(command);
            StationCommand capture = Next(command, StepKind.Capture); Task<StationReply?> failed = port.ExecuteAsync(capture);
            Check(await Fails(() => failed) && port.Snapshot.OutstandingLeases == 0 && port.Snapshot.ProcessingSubmissions == 0,
                "bad frame identity/quality refused after returning borrowed buffer");
            Check(ReferenceEquals(failed, port.ExecuteAsync(capture)) && await port.ObserveAsync(capture) is null && port.Snapshot.FramesAcquired == 1,
                "failed capture preserved, no fallback latest frame or trigger retry");
        }
    }
    private static async Task ProcessingFailureAsync(bool malformed)
    {
        VisionStationPort port = new(PlainPgm.LoadFixture(), measurement: (image, plan) =>
            malformed ? ValueTask.FromResult(image.Measure(plan.Region, plan.Threshold) with { Mean = double.NaN }) : throw new IOException("injected processor failure"));
        VisionStationSession session = await VisionStationSession.OpenAsync(DirectoryName(), port, new ManualStationClock()); session.OpenReplay();
        await session.StartAsync("bad-process", "demo-ok-v1"); await Until(session, s => s.Host.Station.Terminal is not null && !s.Host.TaskActive);
        Check(session.Snapshot.Host.Station.Terminal!.Reason == "StepFailed:Inspect" && session.Snapshot.Host.Station.Pending is null,
            "worker failure is ended inspection failure, not NG or uncertain task");
        Check(session.Vision.ProcessingError is not null && session.Snapshot.Host.Station.Judgment is null && session.Vision.PublishCalls == 0,
            "failed/invalid measurements never manufacture verdict or result ACK");
        await session.SaveResultAsync(); await session.AcknowledgeVisionAfterSavedAsync(); await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.ConfirmedNotPublished); await Close(session);
    }
    private static async Task LateProcessingAsync()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously), release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        VisionStationPort port = new(PlainPgm.LoadFixture(), measurement: async (image, plan) =>
        { entered.TrySetResult(); await release.Task; return image.Measure(plan.Region, plan.Threshold); });
        ManualStationClock clock = new(); VisionStationSession session = await VisionStationSession.OpenAsync(DirectoryName(), port, clock); session.OpenReplay();
        await session.StartAsync("late-inspection", "demo-ok-v1"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check(session.Vision.OutstandingLeases == 0 && session.Vision.RetainedResults == 1 && session.Vision.TaskActive, "owned pixels reach async worker after lease returned");
        Check(await Fails(async () => await port.DisposeAsync()), "running processor forbids early source/worker disposal");
        clock.Advance(TimeSpan.FromSeconds(3)); await Until(session, s => s.Host.Station.Stage == StationStage.Faulted);
        session.RequestClose(); await Until(session, s => s.Host.Station.Stage == StationStage.Closing);
        Check(!await session.CloseStepAsync() && !session.Vision.Disposed, "close saves original fault but waits processor");
        release.TrySetResult(); await Close(session);
        Check(session.Snapshot.Host.Station.Terminal!.Outcome == CycleOutcome.Faulted && session.Snapshot.Host.Station.Judgment is null && session.Vision.PublishCalls == 0,
            "late computed OK cannot rewrite local fault or publish result");
        Check(session.Vision.Disposed && session.Vision.RetainedResults == 1 && !session.Vision.CycleAcknowledged,
            "close drains worker without pretending original receipt was acknowledged");
    }
    private static async Task LateCaptureAsync()
    {
        TaskCompletionSource copied = new(TaskCreationOptions.RunContinuationsAsynchronously), release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        VisionStationPort port = new(PlainPgm.LoadFixture(), afterWork: step =>
        { if (step != StepKind.Capture) return Task.CompletedTask; copied.TrySetResult(); return release.Task; });
        ManualStationClock clock = new(); VisionStationSession session = await VisionStationSession.OpenAsync(DirectoryName(), port, clock); session.OpenReplay();
        await session.StartAsync("late-frame", "demo-ng-v1"); await copied.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(1)); await Until(session, s => s.Host.Station.Stage == StationStage.Faulted);
        session.RequestClose(); await Until(session, s => s.Host.Station.Stage == StationStage.Closing); await session.CloseStepAsync();
        release.TrySetResult(); await Close(session);
        Check(session.Snapshot.Host.Station.FrameId is null && session.Vision.FrameId is not null && session.Vision.ProcessingSubmissions == 0,
            "late capture metadata does not become accepted station frame or trigger inspection");
        Check(session.Vision.FramesAcquired == 1 && session.Vision.OutstandingLeases == 0 && session.Vision.Disposed,
            "late capture no reacquire, all borrowed buffer and source resources closed");
    }
    private static async Task<bool> Fails(Func<Task> work)
    { try { await work(); return false; } catch (Exception error) when (error is InvalidOperationException or InvalidDataException or ArgumentException) { return true; } }
    private static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("Vision integration check failed: " + label); count++; }
    public static async Task DemoAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "hnova-station-vision-" + Guid.NewGuid().ToString("N"));
        VisionStationPort port = new(); VisionStationSession session = await VisionStationSession.OpenAsync(directory, port, new ManualStationClock()); session.OpenReplay();
        Console.WriteLine("OWNED MONO8 REPLAY ONLY: no camera/native SDK/network/model. Files retained at " + directory);
        for (int i = 1; i <= 2; i++)
        {
            await session.StartAsync("part-" + i, "demo-ok-v1"); await Until(session, s => s.Host.Station.Stage == StationStage.Completed);
            Console.WriteLine($"Cycle {i}: frame={session.Vision.FrameStamp!.Value.Number}; mean={session.Vision.Measurement!.Mean:F1}; verdict={session.Snapshot.Host.Station.Judgment}; leases={session.Vision.OutstandingLeases}");
            await session.SaveResultAsync(); await session.AcknowledgeVisionAfterSavedAsync(); await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer);
        }
        await Close(session); Console.WriteLine($"Closed={session.Snapshot.Closed}; processed={session.Vision.MeasureCalls}; retained={session.Vision.RetainedResults}");
    }
}
