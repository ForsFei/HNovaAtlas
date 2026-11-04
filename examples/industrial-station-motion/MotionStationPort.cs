using IndustrialMotion;
using IndustrialStation;

namespace IndustrialStationMotion;

public sealed record MotionPositionRecipe(string Version, double TargetMillimetres, double VelocityMillimetresPerSecond);
public sealed record MotionPortSnapshot(string AxisState, string AxisResult, bool DataValid, bool Enabled, bool Homed,
    bool ReferenceQualified, bool Busy, bool ReadyForNewCycle, double CommandPosition, double? FeedbackPosition,
    double? Target, ulong ConnectionGeneration, ulong AxisSequence, OperationKey? Operation, string Observation,
    string Reason, int HomeCalls, int MoveCalls, int StopCalls, bool Disposed);

// Pure simulation owner: every SimAxis call is serialized by gate; AdvanceSimulation is an explicit test clock pump.
public sealed class MotionStationPort : IStationAsyncPort
{
    private sealed record Binding(StationCommand Command, CommandKey AxisKey, MotionAttempt Attempt, TaskCompletionSource<StationReply?> Promise);
    public static IReadOnlyList<MotionPositionRecipe> Positions { get; } = Array.AsReadOnly(new[]
    { new MotionPositionRecipe("demo-ok-v1", 1, 5), new MotionPositionRecipe("demo-ng-v1", 2, 5) });
    private readonly object gate = new();
    private readonly SimAxis axis;
    private readonly RecipeSimulationPort simulation = new(StationApplication.Recipes);
    private MotionAttempt? reference;
    private TaskCompletionSource<bool>? referencePromise;
    private Binding? move;
    private double time;
    private bool referenceQualified, stopRequested, disposed;
    private int homeCalls, moveCalls, stopCalls;
    public MotionStationPort() : this(new AxisSettings()) { }
    internal MotionStationPort(AxisSettings settings) { axis = new(settings, initialPosition: 1); }
    public MotionPortSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                AxisSnapshot s = axis.Read(); MotionAttempt? observing = reference is not null && s.Key == reference.Key ? reference : move?.Attempt ?? reference;
                return new(s.State.ToString(), s.Result.ToString(), s.DataValid, s.Enabled, s.Homed, referenceQualified, s.Busy,
                    !disposed && s.DataValid && s.Enabled && s.Homed && referenceQualified && s.State == AxisState.Ready &&
                    (move is null || move.Promise.Task.IsCompleted), s.CommandPosition, s.FeedbackPosition, s.Target,
                    s.Key.ConnectionGeneration, s.Key.Sequence, move?.Command.Key, observing?.State.ToString() ?? "None", observing?.Reason ?? "NotPrepared",
                    homeCalls, moveCalls, stopCalls, disposed);
            }
        }
    }
    public void OpenSimulation() { lock (gate) { EnsureAlive(); axis.Open(); referenceQualified = false; } }
    public void EnableSimulation() { lock (gate) { EnsureAlive(); axis.Enable(); } }
    public Task<bool> BeginReferenceSimulation()
    {
        lock (gate)
        {
            EnsureAlive(); AxisSnapshot baseline = axis.Read();
            if (baseline.Busy || referencePromise?.Task.IsCompleted == false || move?.Promise.Task.IsCompleted == false)
                throw new InvalidOperationException("Previous model task still active.");
            CommandKey predicted = new(baseline.Key.ConnectionGeneration, checked(baseline.Key.Sequence + 1));
            MotionAttempt prepared = new(ObservedMotionKind.Home, predicted, Sample(), time);
            CommandKey actual = axis.Home();
            if (actual != predicted) throw new InvalidOperationException("Single-owner key prediction failed.");
            reference = prepared; referenceQualified = false; stopRequested = false; homeCalls++;
            referencePromise = new(TaskCreationOptions.RunContinuationsAsynchronously);
            prepared.RecordDispatch(DispatchEvidence.Acknowledged, time); return referencePromise.Task;
        }
    }
    public Task<StationReply?> ExecuteAsync(StationCommand command)
    {
        lock (gate)
        {
            EnsureAlive(); Validate(command);
            if (command.Key.Step != StepKind.Move)
            {
                if (move is null || move.Command.Key.Session != command.Key.Session || move.Command.Key.Cycle != command.Key.Cycle ||
                    move.Command.Order != command.Order || !move.Promise.Task.IsCompletedSuccessfully || move.Promise.Task.Result?.Success != true)
                    throw new InvalidOperationException("No matching successful move.");
                return simulation.ExecuteAsync(command); // No axis mutation; simulated image/result stages only.
            }
            if (move is not null)
            {
                if (move.Command.Key == command.Key)
                {
                    if (move.Command != command) throw new InvalidOperationException("SameOperationPayloadConflict");
                    return move.Promise.Task; // Same binding and task; never dispatch twice.
                }
                if (!move.Promise.Task.IsCompleted || command.Key.Session != move.Command.Key.Session || command.Key.Cycle <= move.Command.Key.Cycle ||
                    command.Key.Operation <= move.Command.Key.Operation) throw new InvalidOperationException("OldOrConcurrentMotionOperation");
            }
            if (!Snapshot.ReadyForNewCycle) throw new InvalidOperationException("Open/enable/qualified reference required.");
            MotionPositionRecipe recipe = Positions.Single(p => p.Version == command.Order.RecipeVersion);
            AxisSnapshot before = axis.Read(); CommandKey key = new(before.Key.ConnectionGeneration, checked(before.Key.Sequence + 1));
            MotionAttempt attempt = new(ObservedMotionKind.Move, key, Sample(), time, recipe.TargetMillimetres, requireFeedback: true);
            CommandKey dispatched = axis.MoveAbsolute(recipe.TargetMillimetres, recipe.VelocityMillimetresPerSecond);
            if (dispatched != key) throw new InvalidOperationException("Single-owner key prediction failed.");
            TaskCompletionSource<StationReply?> promise = new(TaskCreationOptions.RunContinuationsAsynchronously);
            move = new(command, key, attempt, promise); stopRequested = false; moveCalls++;
            attempt.RecordDispatch(DispatchEvidence.Acknowledged, time); return promise.Task;
        }
    }
    public Task<StationReply?> ObserveAsync(StationCommand command)
    {
        lock (gate)
        {
            EnsureAlive(); Validate(command);
            if (command.Key.Step != StepKind.Move) return simulation.ObserveAsync(command);
            if (move is null || move.Command != command || axis.Read().Key.ConnectionGeneration != move.AxisKey.ConnectionGeneration || !axis.Read().DataValid)
                return Task.FromResult<StationReply?>(null);
            return Task.FromResult(move.Promise.Task.IsCompletedSuccessfully ? move.Promise.Task.Result : null);
        }
    }
    public void AdvanceSimulation(double seconds)
    {
        lock (gate)
        {
            EnsureAlive(); axis.Tick(seconds); time += seconds;
            ObserveCurrent();
        }
    }
    private void ObserveCurrent()
    {
        AxisSnapshot s = axis.Read();
        if (!s.DataValid || !s.Enabled || !s.Homed && reference?.Terminal == true || s.State == AxisState.Faulted) referenceQualified = false;
        if (reference is { Terminal: false }) reference.Observe(Sample(), time);
        if (referencePromise?.Task.IsCompleted == false && reference?.Terminal == true)
        {
            referenceQualified = reference.ReferenceEstablished && s.Homed && s.DataValid;
            referencePromise.TrySetResult(referenceQualified);
        }
        if (move is null || move.Promise.Task.IsCompleted) return;
        move.Attempt.Observe(Sample(), time);
        if (move.Attempt.State == AttemptState.Unknown) { referenceQualified = false; move.Promise.TrySetResult(null); return; }
        if (move.Attempt.State == AttemptState.Completed && s.CompletedFor(move.AxisKey) && move.Attempt.Evidence == AttemptEvidence.FeedbackAndInPosition)
            move.Promise.TrySetResult(Reply(move.Command, true));
        else if (move.Attempt.State is AttemptState.Failed or AttemptState.Stopped && s.DataValid && s.Key == move.AxisKey && !s.Busy &&
                 s.Result is CommandResult.Faulted or CommandResult.Stopped)
            move.Promise.TrySetResult(Reply(move.Command, false));
    }
    private ObservedAxis Sample()
    {
        AxisSnapshot s = axis.Read();
        // ONLY this ideal model supports this normalization. Not an E3032 SDK mapping.
        return new(time, s.Key.ConnectionGeneration, s.DataValid, BusError: s.DataValid ? (ushort)0 : (ushort)1,
            DriveState: s.Enabled ? (ushort)4 : (ushort)1, AxisError: s.Result == CommandResult.Faulted ? (ushort)1 : (ushort)0,
            IsStopped: !s.Busy, HomeResult: s.Homed ? (ushort)1 : (ushort)0, StopReason: stopRequested && !s.Busy ? 13 : 0,
            CommandPosition: s.CommandPosition, FeedbackPosition: s.FeedbackPosition, InPosition: currentInPosition);
    }
    private bool currentInPosition = true;
    public void RequestOrdinaryStopSimulation()
    {
        lock (gate)
        {
            EnsureAlive(); if (!axis.Read().Busy) return;
            axis.RequestStop(); stopRequested = true; stopCalls++;
        }
    }
    public void ResetSimulationFault()
    {
        lock (gate)
        {
            EnsureAlive();
            if (referencePromise?.Task.IsCompleted == false || move?.Promise.Task.IsCompleted == false)
                throw new InvalidOperationException("Observe previous model task before resetting fault.");
            axis.ResetFault(); referenceQualified = false;
        }
    }
    internal void SetInputsForChecks(AxisInputs inputs)
    {
        lock (gate) { currentInPosition = inputs.InPosition; axis.SetInputs(inputs); if (!axis.Read().Homed || !axis.Read().DataValid) referenceQualified = false; }
    }
    internal void SetFollowingOffsetForChecks(double offset) { lock (gate) axis.InjectFollowingOffset(offset); }
    internal void LoseObservationForChecks(double duration)
    {
        lock (gate)
        {
            for (double elapsed = 0; elapsed + 1e-9 < duration; elapsed += .01) { axis.Tick(.01); time += .01; }
            referenceQualified = false;
            if (move is not null && !move.Promise.Task.IsCompleted) { move.Attempt.CheckDeadline(time); if (move.Attempt.Terminal) move.Promise.TrySetResult(null); }
        }
    }
    private static StationReply Reply(StationCommand command, bool success) => new(command.Key, command.Order.Workpiece, command.Order.RecipeVersion, success, null, null);
    private static void Validate(StationCommand command)
    {
        if (command.Key.Session == Guid.Empty || command.Key.Cycle <= 0 || command.Key.Operation <= 0 || !Enum.IsDefined(command.Key.Step) ||
            !Positions.Any(p => p.Version == command.Order.RecipeVersion) || command.Key.Step == StepKind.Move && (command.FrameId is not null || command.Judgment is not null))
            throw new ArgumentException("Invalid station operation or fixed motion recipe.");
    }
    private void EnsureAlive() => ObjectDisposedException.ThrowIf(disposed, this);
    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed) return;
            if (axis.Read().Busy || referencePromise?.Task.IsCompleted == false || move?.Promise.Task.IsCompleted == false)
                throw new InvalidOperationException("Observe model task/axis idle before disposal.");
            if (axis.Read().DataValid) axis.Close();
            axis.Dispose(); referenceQualified = false; disposed = true;
        }
        await simulation.DisposeAsync().ConfigureAwait(false);
    }
}
