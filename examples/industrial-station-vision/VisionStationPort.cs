using IndustrialStation;
using IndustrialVision;

namespace IndustrialStationVision;

public sealed record VisionRecipe(string Version, PixelRoi Region, byte Threshold, double MinimumMean, int MinimumBrightPixels)
{
    public ProcessingRecipe Processing => new(Version, 1, Region, Threshold);
}
public sealed record VisionPortSnapshot(bool Open, bool ReadyForNextCycle, bool TaskActive, OperationKey? CaptureOperation,
    Guid? FrameId, FrameStamp? FrameStamp, string? Workpiece, string? RecipeVersion, PixelStatistics? Measurement,
    Verdict? Judgment, string? ProcessingError, ulong FramesAcquired, int OutstandingLeases, int ProcessingSubmissions,
    int MeasureCalls, int RetainedResults, int PublishCalls, bool CycleAcknowledged, bool Disposed, int SimulatedMoveCalls, int ExternalMoveHandoffs);

// Single station, one retained cycle, managed Mono8 only. No camera/SDK/native algorithm.
public sealed class VisionStationPort : IStationAsyncPort
{
    private sealed record Call(StationCommand Command, TaskCompletionSource<StationReply?> Completion);
    private sealed class Cycle(StationCommand first)
    {
        public StationCommand First { get; } = first;
        public Dictionary<StepKind, Call> Calls { get; } = new();
        public CaptureResult? Capture;
        public Guid? FrameId;
        public FrameStamp? Stamp;
        public InspectionPipeline? Pipeline;
        public InspectionResult? Result;
        public Verdict? Judgment;
        public bool Released;
    }
    public static IReadOnlyList<VisionRecipe> Recipes { get; } = Array.AsReadOnly(new[]
    { new VisionRecipe("demo-ok-v1", new(1, 1, 2, 2), 200, 200, 3), new VisionRecipe("demo-ng-v1", new(5, 1, 2, 2), 200, 200, 3) });
    private readonly object gate = new();
    private readonly OfflineFrameSource source;
    private readonly RecipeSimulationPort simulation = new(StationApplication.Recipes);
    private readonly Func<Mono8Image, ProcessingRecipe, ValueTask<PixelStatistics>>? measurement;
    private readonly Func<StepKind, Task>? afterWork;
    private readonly Func<CaptureFrame, CaptureFrame>? transform;
    private readonly Dictionary<string, InspectionPipeline> pipelines = new(StringComparer.Ordinal);
    private SingleCaptureSession? captureSession;
    private Guid cameraEpoch;
    private Cycle? cycle;
    private bool active, closing, disposed;
    private int submissions, measureCalls, publishCalls, simulatedMoveCalls, externalMoveHandoffs;
    private double replayTime;
    private Task? disposal;
    public VisionStationPort() : this(PlainPgm.LoadFixture(), null, null, null) { }
    internal VisionStationPort(Mono8Image fixture,
        Func<Mono8Image, ProcessingRecipe, ValueTask<PixelStatistics>>? measurement = null,
        Func<StepKind, Task>? afterWork = null, Func<CaptureFrame, CaptureFrame>? transform = null)
    {
        byte[] inverse = fixture.CopyPixels(); for (int i = 0; i < inverse.Length; i++) inverse[i] = (byte)(255 - inverse[i]);
        source = new([fixture, Mono8Image.FromPacked(fixture.Width, fixture.Height, inverse)], paddingX: 2);
        foreach (VisionRecipe recipe in Recipes) recipe.Region.Validate(fixture.Width, fixture.Height);
        this.measurement = measurement; this.afterWork = afterWork; this.transform = transform;
    }
    public VisionPortSnapshot Snapshot
    {
        get
        {
            lock (gate) return new(source.IsOpen, source.IsOpen && !closing && (cycle is null || cycle.Released) && !active,
                active, cycle?.Calls.GetValueOrDefault(StepKind.Capture)?.Command.Key, cycle?.FrameId, cycle?.Stamp,
                cycle?.First.Order.Workpiece, cycle?.First.Order.RecipeVersion, cycle?.Result?.Measurement, cycle?.Judgment,
                cycle?.Result?.ErrorCode, source.FramesAcquired, source.OutstandingLeases, submissions,
                Volatile.Read(ref measureCalls), pipelines.Values.Sum(p => p.RetainedCount), publishCalls, cycle?.Released ?? true, disposed, simulatedMoveCalls, externalMoveHandoffs);
        }
    }
    public void OpenReplay()
    {
        lock (gate)
        {
            EnsureAlive(); if (source.IsOpen || cycle is not null) throw new InvalidOperationException("Replay already opened/used.");
            source.Open(); cameraEpoch = Guid.NewGuid();
            captureSession = new(cameraEpoch, source.Session, 1, source.Width, source.Height, startupReviewed: true);
            foreach (VisionRecipe recipe in Recipes)
                pipelines.Add(recipe.Version, new(captureSession.OwnerSession, source.Width, source.Height, recipe.Processing, capacity: 1,
                    measure: (image, plan) =>
                    {
                        Interlocked.Increment(ref measureCalls);
                        return measurement is null ? ValueTask.FromResult(image.Measure(plan.Region, plan.Threshold)) : measurement(image, plan);
                    }));
        }
    }
    // Trusted composition ingress: records a qualified external result, NEVER dispatches another move.
    // Structural checks do not establish physical motion evidence; the caller must own and qualify the motion task.
    public bool RecordQualifiedExternalMove(StationCommand command, StationReply result)
    {
        lock (gate)
        {
            EnsureAlive(); Validate(command);
            if (!source.IsOpen || closing || active) throw new InvalidOperationException("VisionNotReadyForMoveHandoff");
            if (command.Key.Step != StepKind.Move || command.FrameId is not null || command.Judgment is not null ||
                result.Key != command.Key || result.Workpiece != command.Order.Workpiece || result.RecipeVersion != command.Order.RecipeVersion ||
                !result.Success || result.FrameId is not null || result.Judgment is not null)
                throw new ArgumentException("Matching successful Move evidence without frame/verdict required.");
            if (cycle is not null && cycle.First.Key == command.Key)
            {
                if (cycle.First != command || !cycle.Calls[StepKind.Move].Completion.Task.IsCompletedSuccessfully ||
                    cycle.Calls[StepKind.Move].Completion.Task.Result != result) throw new InvalidOperationException("MoveHandoffConflict");
                return false;
            }
            if (cycle is not null && (!cycle.Released || command.Key.Session != cycle.First.Key.Session || command.Key.Cycle <= cycle.First.Key.Cycle ||
                command.Key.Operation <= cycle.First.Key.Operation)) throw new InvalidOperationException("RetainedOrStaleMoveHandoff");
            Cycle next = new(command); TaskCompletionSource<StationReply?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            completion.SetResult(result); next.Calls.Add(StepKind.Move, new(command, completion)); cycle = next; externalMoveHandoffs++; return true;
        }
    }
    public Task<StationReply?> ExecuteAsync(StationCommand command)
    {
        Call call; Cycle retained;
        lock (gate)
        {
            EnsureAlive(); Validate(command);
            if (!source.IsOpen || closing) throw new InvalidOperationException("ReplayClosed");
            if (cycle is null || cycle.First.Key.Session != command.Key.Session || cycle.First.Key.Cycle != command.Key.Cycle)
            {
                if (command.Key.Step != StepKind.Move || command.FrameId is not null || command.Judgment is not null ||
                    cycle is not null && (!cycle.Released || command.Key.Session != cycle.First.Key.Session || command.Key.Cycle <= cycle.First.Key.Cycle || command.Key.Operation <= cycle.First.Key.Operation))
                    throw new InvalidOperationException("NewCycleRequiresReleasedSlotAndMove");
                cycle = new(command);
            }
            retained = cycle;
            if (retained.First.Order != command.Order) throw new InvalidOperationException("CapturedOrderConflict");
            if (retained.Calls.TryGetValue(command.Key.Step, out Call? previous))
            {
                if (previous.Command != command) throw new InvalidOperationException("SameStepPayloadConflict");
                return previous.Completion.Task;
            }
            if (retained.Released || active) throw new InvalidOperationException("RetainedOrActiveVisionCall");
            if (command.Key.Step != StepKind.Move)
            {
                StepKind preceding = command.Key.Step switch { StepKind.Capture => StepKind.Move, StepKind.Inspect => StepKind.Capture, _ => StepKind.Inspect };
                if (!retained.Calls.TryGetValue(preceding, out Call? before) || !before.Completion.Task.IsCompletedSuccessfully ||
                    before.Completion.Task.Result?.Success != true || command.Key.Operation <= before.Command.Key.Operation)
                    throw new InvalidOperationException("PreviousStepNotSuccessfullyObserved");
                if (command.Key.Step == StepKind.Capture && (command.FrameId is not null || command.Judgment is not null) ||
                    command.Key.Step == StepKind.Inspect && (command.FrameId != retained.FrameId || command.FrameId is null || command.Judgment is not null) ||
                    command.Key.Step == StepKind.PublishResult && (command.FrameId != retained.FrameId || command.Judgment != retained.Judgment))
                    throw new InvalidOperationException("FrameOrVerdictMismatch");
            }
            call = new(command, new(TaskCreationOptions.RunContinuationsAsynchronously)); retained.Calls.Add(command.Key.Step, call); active = true;
        }
        _ = RunAsync(retained, call); // Every exception is transferred to Completion; no per-frame Task.Run.
        return call.Completion.Task;
    }
    private async Task RunAsync(Cycle retained, Call call)
    {
        StationReply? reply = null; Exception? failure = null;
        try
        {
            StationCommand command = call.Command;
            switch (command.Key.Step)
            {
                case StepKind.Move:
                    lock (gate) simulatedMoveCalls++;
                    reply = await simulation.ExecuteAsync(command).ConfigureAwait(false); break;
                case StepKind.Capture:
                    lock (gate)
                    {
                        replayTime += .001; // Synthetic replay ordering only, not a camera freshness clock.
                        SingleCaptureSession session = captureSession!;
                        if (!session.Update(new(replayTime, cameraEpoch, source.Session, 1, AcquisitionMode.SoftwareSingle, true, true, true, true), replayTime))
                            throw new InvalidDataException("Offline capture context rejected.");
                        CaptureRequest request = session.Prepare(command.Order.Workpiece, replayTime) ?? throw new InvalidDataException("Capture not prepared.");
                        if (session.TakeTrigger(replayTime) != request) throw new InvalidDataException("Trigger not transferred.");
                        if (!session.RecordDispatch(request.Key, TriggerDispatchEvidence.Acknowledged, replayTime)) throw new InvalidDataException("Dispatch record rejected.");
                        OwnedFrame owned;
                        using (FrameLease lease = source.Acquire()) owned = lease.CopyToOwned(); // Return borrowed buffer BEFORE any asynchronous processing.
                        ulong sequence = owned.Stamp.Number; // Defined by this replay fixture, not inferred for a real camera.
                        if (!session.Associate(new(request.Key, cameraEpoch, sequence, Qualified: true), replayTime)) throw new InvalidDataException("Replay association rejected.");
                        CaptureFrame packet = new(owned, cameraEpoch, 1, sequence, replayTime, Complete: true, IdentityQualified: true);
                        if (transform is not null) packet = transform(packet);
                        if (session.ObserveFrame(packet, replayTime) != CaptureFrameDecision.Matched) throw new InvalidDataException("Frame correlation rejected.");
                        retained.Capture = session.TakeForProcessing(replayTime) ?? throw new InvalidDataException("No qualified pixels transferred.");
                        if (!session.ReleaseCapture(request.Key, replayTime)) throw new InvalidDataException("Capture transfer release rejected.");
                        retained.FrameId = Guid.NewGuid(); retained.Stamp = owned.Stamp;
                        reply = Reply(command, true, retained.FrameId, null);
                    }
                    break;
                case StepKind.Inspect:
                    InspectionSubmission admission;
                    lock (gate)
                    {
                        retained.Pipeline = pipelines[command.Order.RecipeVersion];
                        admission = retained.Pipeline.TrySubmit(retained.Capture!);
                        if (admission.Admission != InspectionAdmission.Accepted || admission.Result is null) throw new InvalidOperationException("InspectionNotAccepted:" + admission.Admission);
                        submissions++;
                    }
                    InspectionResult result = await admission.Result.ConfigureAwait(false);
                    lock (gate)
                    {
                        retained.Result = result;
                        if (result.PipelineSession != retained.Pipeline!.Session || result.Request != retained.Capture!.Request ||
                            result.Frame != retained.Stamp || result.CameraEpoch != cameraEpoch || result.TriggerSequence != retained.Capture.Evidence.TriggerSequence ||
                            result.Recipe != Recipes.Single(r => r.Version == command.Order.RecipeVersion).Processing)
                            throw new InvalidDataException("Inspection identity changed.");
                        if (result.Stage == ProcessingStage.Completed && result.Measurement is { } stats)
                        {
                            VisionRecipe recipe = Recipes.Single(r => r.Version == command.Order.RecipeVersion);
                            retained.Judgment = stats.Mean >= recipe.MinimumMean && stats.AtOrAboveThreshold >= recipe.MinimumBrightPixels ? Verdict.Ok : Verdict.Ng;
                            reply = Reply(command, true, retained.FrameId, retained.Judgment);
                        }
                        else reply = Reply(command, false, retained.FrameId, null); // Real worker terminal failure, not an invented timeout result.
                    }
                    break;
                case StepKind.PublishResult:
                    lock (gate) { publishCalls++; reply = Reply(command, true, retained.FrameId, retained.Judgment); } // Local simulated ACK only.
                    break;
            }
            if (afterWork is not null) await afterWork(call.Command.Key.Step).ConfigureAwait(false);
        }
        catch (Exception error) { failure = error; }
        lock (gate)
        {
            active = false;
            if (failure is null) call.Completion.TrySetResult(reply); else call.Completion.TrySetException(failure);
        }
    }
    public Task<StationReply?> ObserveAsync(StationCommand command)
    {
        lock (gate)
        {
            EnsureAlive();
            if (cycle is null || !cycle.Calls.TryGetValue(command.Key.Step, out Call? call) || call.Command != command) return Task.FromResult<StationReply?>(null);
            return Task.FromResult(call.Completion.Task.IsCompletedSuccessfully ? call.Completion.Task.Result : null); // Never acquire or submit again.
        }
    }
    // Export only the retained, successfully completed LOCAL cycle before acknowledgment.
    public VisionEvidence ExportEvidence(ApplicationSnapshot application)
    {
        lock (gate)
        {
            EnsureAlive(); Cycle c = cycle ?? throw new InvalidOperationException("No cycle");
            StationSnapshot s = application.Host.Station;
            if (active || closing || c.Released || application.Busy || application.CloseRequested || application.Host.TaskActive ||
                s.Pending is not null || s.Stage != StationStage.Completed || s.Terminal is not { Outcome: CycleOutcome.Completed } terminal ||
                s.Session != c.First.Key.Session || s.Cycle != c.First.Key.Cycle || s.Order != c.First.Order ||
                terminal.FrameId != c.FrameId || terminal.Judgment != c.Judgment || c.Capture is null ||
                c.Result is not { Stage: ProcessingStage.Completed, Measurement: not null } result || c.FrameId is null)
                throw new InvalidOperationException("Matching completed retained inspection required");
            VisionRecipe recipe = Recipes.Single(r => r.Version == c.First.Order.RecipeVersion);
            return new(terminal, c.FrameId.Value, c.Capture.Evidence.Frame, result, recipe);
        }
    }
    public OwnedFrame ExportAbnormalFrame(ApplicationSnapshot application)
    {
        lock (gate)
        {
            EnsureAlive(); Cycle c = cycle ?? throw new InvalidOperationException("No retained cycle");
            StationSnapshot s = application.Host.Station;
            if (active || closing || c.Released || application.Busy || application.CloseRequested || application.Host.TaskActive ||
                s.Pending is not null || s.Terminal is not { Outcome: CycleOutcome.Faulted or CycleOutcome.Stopped } t ||
                t.Session != c.First.Key.Session || t.Cycle != c.First.Key.Cycle || t.Order != c.First.Order ||
                c.Capture is null || c.FrameId is null || c.Capture.Evidence.Frame.Stamp != c.Stamp ||
                t.FrameId is not null && t.FrameId != c.FrameId)
                throw new InvalidOperationException("Settled matching abnormal frame required before acknowledgment");
            return c.Capture.Evidence.Frame;
        }
    }
    public Task<bool> AcknowledgeAfterSavedAsync(ApplicationSnapshot application)
    {
        lock (gate)
        {
            EnsureAlive(); Cycle retained = cycle ?? throw new InvalidOperationException("No cycle."); StationSnapshot s = application.Host.Station;
            RecordedCycle? record = application.Ledger.Cycles.SingleOrDefault(c => c.Session == retained.First.Key.Session && c.Cycle == retained.First.Key.Cycle);
            if (active || application.Busy || application.Host.TaskActive || s.Pending is not null || !s.TerminalConsumed || s.Terminal is null ||
                s.Session != retained.First.Key.Session || s.Cycle != retained.First.Key.Cycle || s.Order != retained.First.Order || record?.Terminal != s.Terminal)
                throw new InvalidOperationException("Save and consume matching local terminal after actual task/pending settle.");
            if (retained.Released) return Task.FromResult(true);
            if (retained.Result is not null && !retained.Pipeline!.Acknowledge(retained.Result)) throw new InvalidOperationException("Original pipeline receipt not retained.");
            retained.Capture = null; retained.Released = true; return Task.FromResult(true);
        }
    }
    private static StationReply Reply(StationCommand command, bool success, Guid? frame, Verdict? verdict) => new(command.Key, command.Order.Workpiece, command.Order.RecipeVersion, success, frame, verdict);
    private static void Validate(StationCommand command)
    {
        if (command.Key.Session == Guid.Empty || command.Key.Cycle <= 0 || command.Key.Operation <= 0 || !Enum.IsDefined(command.Key.Step) ||
            !Recipes.Any(r => r.Version == command.Order.RecipeVersion)) throw new ArgumentException("Invalid station command or fixed vision recipe.");
    }
    private void EnsureAlive() => ObjectDisposedException.ThrowIf(disposed, this);
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (active) throw new InvalidOperationException("Cannot dispose while actual capture/processing task is active.");
            disposal ??= DisposeCoreAsync(); return new(disposal);
        }
    }
    private async Task DisposeCoreAsync()
    {
        closing = true; foreach (InspectionPipeline pipeline in pipelines.Values) pipeline.Complete();
        await Task.WhenAll(pipelines.Values.Select(p => p.Completion)).ConfigureAwait(false);
        lock (gate) { source.Dispose(); captureSession?.Invalidate(); if (cycle is not null) cycle.Capture = null; disposed = true; }
        await simulation.DisposeAsync().ConfigureAwait(false);
    }
}
