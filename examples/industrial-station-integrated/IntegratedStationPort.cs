using IndustrialStation;
using IndustrialStationMotion;
using IndustrialStationVision;

namespace IndustrialStationIntegrated;

public sealed record IntegratedPortSnapshot(MotionPortSnapshot Motion, VisionPortSnapshot Vision,
    OperationKey? MoveOperation, bool MoveHandedToVision, bool TaskActive, bool Disposed);

// Routes ONE station task. Motion owns Move, vision only records that qualified result before later Capture/Inspect.
public sealed class IntegratedStationPort : IStationAsyncPort
{
    private sealed record Call(StationCommand Command, TaskCompletionSource<StationReply?> Completion);
    private readonly object gate = new();
    private readonly MotionStationPort motion = new();
    private readonly VisionStationPort vision = new();
    private readonly Dictionary<StepKind, Call> calls = new();
    private readonly Func<StepKind, Task>? afterStep;
    private StationCommand? move;
    private Task<bool>? reference;
    private bool handed, active, closing, disposed;
    private Task? disposal;
    public IntegratedStationPort() { }
    internal IntegratedStationPort(Func<StepKind, Task> afterStep) { this.afterStep = afterStep; }
    public IndustrialVision.OwnedFrame ExportAbnormalFrame(ApplicationSnapshot application)
    {
        lock (gate)
        {
            EnsureAlive(); if (active || closing || !handed) throw new InvalidOperationException("Composite not settled");
            return vision.ExportAbnormalFrame(application);
        }
    }
    public VisionEvidence ExportEvidence(ApplicationSnapshot application)
    {
        lock (gate)
        {
            EnsureAlive();
            if (active || closing || !handed) throw new InvalidOperationException("Composite not settled");
            return vision.ExportEvidence(application);
        }
    }
    public IntegratedPortSnapshot Snapshot { get { lock (gate) return new(motion.Snapshot, vision.Snapshot, move?.Key, handed, active, disposed); } }
    public void OpenMotionSimulation() { lock (gate) { EnsureAlive(); motion.OpenSimulation(); } }
    public void EnableMotionSimulation() { lock (gate) { EnsureAlive(); motion.EnableSimulation(); } }
    public Task<bool> BeginReferenceSimulation() { lock (gate) { EnsureAlive(); reference = motion.BeginReferenceSimulation(); return reference; } }
    public void OpenReplay() { lock (gate) { EnsureAlive(); vision.OpenReplay(); } }
    public void AdvanceSimulation(double seconds) { lock (gate) { EnsureAlive(); motion.AdvanceSimulation(seconds); } }
    // A close pump may race with host-owned disposal. Do not tick an already-closing child module.
    public bool TryAdvanceSimulation(double seconds)
    {
        lock (gate)
        {
            if (closing || disposed) return false;
            motion.AdvanceSimulation(seconds); return true;
        }
    }
    public void RequestOrdinaryStopSimulation() { lock (gate) { EnsureAlive(); motion.RequestOrdinaryStopSimulation(); } }
    public Task<StationReply?> ExecuteAsync(StationCommand command)
    {
        Call call;
        lock (gate)
        {
            EnsureAlive();
            if (move is null || command.Key.Session != move.Key.Session || command.Key.Cycle != move.Key.Cycle)
            {
                if (active || command.Key.Step != StepKind.Move || !motion.Snapshot.ReadyForNewCycle || !vision.Snapshot.ReadyForNextCycle ||
                    move is not null && (command.Key.Session != move.Key.Session || command.Key.Cycle <= move.Key.Cycle || command.Key.Operation <= move.Key.Operation))
                    throw new InvalidOperationException("NewCycleNotReadyOrStale");
                move = command; handed = false; calls.Clear();
            }
            if (move.Order != command.Order) throw new InvalidOperationException("CapturedOrderConflict");
            if (calls.TryGetValue(command.Key.Step, out Call? existing))
            {
                if (existing.Command != command) throw new InvalidOperationException("SameStepConflict");
                return existing.Completion.Task;
            }
            if (active || command.Key.Step != StepKind.Move && !handed) throw new InvalidOperationException("NoQualifiedMoveHandoff");
            call = new(command, new(TaskCreationOptions.RunContinuationsAsynchronously)); calls.Add(command.Key.Step, call); active = true;
        }
        _ = RunAsync(call); return call.Completion.Task;
    }
    private async Task RunAsync(Call call)
    {
        StationReply? result = null; Exception? failure = null;
        try
        {
            if (call.Command.Key.Step == StepKind.Move)
            {
                result = await motion.ExecuteAsync(call.Command).ConfigureAwait(false);
                if (result is { Success: true })
                {
                    lock (gate) { vision.RecordQualifiedExternalMove(call.Command, result); handed = true; }
                }
            }
            else result = await vision.ExecuteAsync(call.Command).ConfigureAwait(false);
            if (afterStep is not null) await afterStep(call.Command.Key.Step).ConfigureAwait(false);
        }
        catch (Exception error) { failure = error; }
        lock (gate)
        {
            active = false;
            if (failure is null) call.Completion.TrySetResult(result); else call.Completion.TrySetException(failure);
        }
    }
    public Task<StationReply?> ObserveAsync(StationCommand command)
    {
        lock (gate)
        {
            EnsureAlive();
            if (!calls.TryGetValue(command.Key.Step, out Call? call) || call.Command != command) return Task.FromResult<StationReply?>(null);
            // Historical evidence only: never dispatch, take a frame, or backfill a missing handoff on this path.
            return Task.FromResult(call.Completion.Task.IsCompletedSuccessfully ? call.Completion.Task.Result : null);
        }
    }
    public Task<bool> AcknowledgeVisionAfterSavedAsync(ApplicationSnapshot application)
    {
        lock (gate)
        {
            EnsureAlive(); if (active) throw new InvalidOperationException("IntegratedTaskStillActive");
            return handed ? vision.AcknowledgeAfterSavedAsync(application) : Task.FromResult(false); // No vision cycle was admitted on failed/unknown Move.
        }
    }
    private void EnsureAlive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (closing) throw new InvalidOperationException("IntegratedPortClosing");
    }
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposal is not null) return new(disposal);
            if (active || motion.Snapshot.Busy || reference?.IsCompleted == false) throw new InvalidOperationException("Model/port task not settled.");
            closing = true; disposal = DisposeCoreAsync(); return new(disposal);
        }
    }
    private async Task DisposeCoreAsync()
    {
        List<Exception> failures = new();
        try { await motion.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { failures.Add(error); }
        try { await vision.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { failures.Add(error); }
        lock (gate) disposed = failures.Count == 0;
        if (failures.Count != 0) throw new AggregateException("Integrated cleanup failed.", failures);
    }
}
