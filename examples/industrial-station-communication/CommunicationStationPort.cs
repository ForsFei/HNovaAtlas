using IndustrialHandshake;
using IndustrialStation;

namespace IndustrialStationCommunication;

public sealed record CommunicationPortSnapshot(OperationKey? Operation, uint? BootId, ushort? RequestId,
    bool RemoteTerminalObserved, bool ReleaseAttempted, bool ReleaseObserved, bool ReadyForNextCycle,
    string LastResolution, int SubmitWrites, int ReleaseWrites, int Connections, bool Disposed);

// One station/one publisher; ONLY the Move step crosses an actual MC TCP loopback connection.
public sealed class CommunicationStationPort : IStationAsyncPort
{
    private sealed record Binding(StationCommand Command, Ticket Ticket, StationReply? Terminal = null,
        bool ReleaseAttempted = false, bool ReleaseObserved = false, bool Released = false);
    private readonly LoopbackController controller;
    private readonly HandshakeClient client;
    private readonly RecipeSimulationPort simulation = new(StationApplication.Recipes);
    private readonly SemaphoreSlim calls = new(1, 1);
    private readonly object state = new();
    private Binding? binding;
    private string resolution = "NotStarted";
    private bool disposed;
    public static CommunicationStationPort CreateLoopback() => new(new LoopbackController());
    internal CommunicationStationPort(LoopbackController controller) { this.controller = controller; client = new(controller); }
    public CommunicationPortSnapshot Snapshot
    {
        get { lock (state) return new(binding?.Command.Key, binding?.Ticket.BootId, binding?.Ticket.RequestId,
            binding?.Terminal is not null, binding?.ReleaseAttempted ?? false, binding?.ReleaseObserved ?? false,
            binding is null || binding.Released, resolution, controller.SubmitWrites, controller.ReleaseWrites, controller.Connections, disposed); }
    }
    public Task<StationReply?> ExecuteAsync(StationCommand command) => Exclusive(async () =>
    {
        Validate(command);
        if (command.Key.Step != StepKind.Move)
        {
            Binding? current = Current;
            if (current is null || current.Command.Key.Session != command.Key.Session || current.Command.Key.Cycle != command.Key.Cycle ||
                current.Command.Order != command.Order || current.Terminal?.Success != true || current.Released)
                throw new InvalidOperationException("No matching completed communication move.");
            return await simulation.ExecuteAsync(command).ConfigureAwait(false);
        }
        Binding? previous = Current;
        if (previous is not null)
        {
            if (previous.Command.Key == command.Key)
            {
                if (previous.Command != command || previous.Released) throw new InvalidOperationException("ConflictOrReleasedOperationNeverReplay");
                return previous.Terminal; // Never allocate/submit a second ticket for the same operation.
            }
            if (!previous.Released || command.Key.Session != previous.Command.Key.Session || command.Key.Cycle <= previous.Command.Key.Cycle ||
                command.Key.Operation <= previous.Command.Key.Operation) throw new InvalidOperationException("RemoteRetainedOrStaleStationOperation");
        }
        IndustrialHandshake.Snapshot status = await client.InspectAsync().ConfigureAwait(false);
        Ticket ticket = HandshakeClient.CreateTicket(status, command.Order.RecipeVersion == "demo-ng-v1" ? (short)600 : (short)300);
        lock (state) binding = new(command, ticket); // Save mapping before attempting submit; in-memory only.
        Observation submitted = await client.SubmitAsync(ticket).ConfigureAwait(false); Note(submitted);
        if (submitted.Resolution != Resolution.Known || submitted.Snapshot?.Reply is not (Reply.Recorded or Reply.Duplicate)) return null;
        Observation finished = await client.WaitAsync(ticket, TimeSpan.FromSeconds(1)).ConfigureAwait(false); Note(finished);
        return Map(finished);
    });
    public Task<StationReply?> ObserveAsync(StationCommand command) => Exclusive(async () =>
    {
        Validate(command);
        if (command.Key.Step != StepKind.Move) return await simulation.ObserveAsync(command).ConfigureAwait(false);
        Binding? current = Current;
        if (current is null || current.Command != command || current.Released) return null;
        Observation observed = await client.QueryAsync(current.Ticket).ConfigureAwait(false); Note(observed);
        return Map(observed); // QueryAsync writes no control/staging words.
    });
    public Task<bool> ResetObservedControlAsync() => Exclusive(async () =>
    {
        Binding current = Current ?? throw new InvalidOperationException("No mapped operation.");
        if (current.Terminal is null && !current.ReleaseObserved) throw new InvalidOperationException("Observe matching terminal first.");
        IndustrialHandshake.Snapshot before = await client.InspectAsync().ConfigureAwait(false);
        if (before.BootId != current.Ticket.BootId) { SetResolution("EpochChanged"); return false; }
        bool reset = before.Control == 0 && before.ControlAck == 0 || await client.ResetObservedControlAsync(current.Ticket).ConfigureAwait(false);
        if (reset && current.ReleaseObserved)
        {
            IndustrialHandshake.Snapshot after = await client.InspectAsync().ConfigureAwait(false);
            if (ReleaseEvidence(after, current) && after.Control == 0 && after.ControlAck == 0)
                lock (state) binding = current with { Released = true };
        }
        SetResolution(reset ? "ControlResetObserved" : "ControlResetUnknown"); return reset;
    });
    public Task<bool> ReleaseAfterSavedAsync(ApplicationSnapshot application) => Exclusive(async () =>
    {
        Binding current = Current ?? throw new InvalidOperationException("No mapped operation.");
        RequireSaved(application, current);
        if (current.Released) return true;
        if (current.ReleaseAttempted) throw new InvalidOperationException("ReleaseUncertain: observe old release, never repeat write2.");
        IndustrialHandshake.Snapshot before = await client.InspectAsync().ConfigureAwait(false);
        if (before.BootId != current.Ticket.BootId || before.Current != current.Ticket || !before.Terminal)
        { SetResolution("MatchingRemoteTerminalRequired"); return false; }
        if (before.Control != 0 || before.ControlAck != 0) throw new InvalidOperationException("ResetObservedControlFirst");
        lock (state) binding = current = current with { ReleaseAttempted = true };
        Observation release = await client.ReleaseAsync(current.Ticket).ConfigureAwait(false); Note(release);
        if (release.Resolution != Resolution.Known || release.Snapshot?.Reply != Reply.Released) return false;
        IndustrialHandshake.Snapshot after = await client.InspectAsync().ConfigureAwait(false);
        bool known = ReleaseEvidence(after, current);
        lock (state) binding = current with { ReleaseObserved = known, Released = known && after.Control == 0 && after.ControlAck == 0 };
        return Current!.Released;
    });
    public Task<bool> ObserveReleaseAsync() => Exclusive(async () =>
    {
        Binding current = Current ?? throw new InvalidOperationException("No mapped operation.");
        if (!current.ReleaseAttempted) return false;
        IndustrialHandshake.Snapshot status = await client.InspectAsync().ConfigureAwait(false);
        bool observed = ReleaseEvidence(status, current);
        lock (state) binding = current with { ReleaseObserved = observed,
            Released = observed && status.Control == 0 && status.ControlAck == 0 };
        SetResolution(observed ? "MatchingReleaseObserved" : "ReleaseNotProven"); return observed;
    });
    private static bool ReleaseEvidence(IndustrialHandshake.Snapshot status, Binding current) =>
        status.BootId == current.Ticket.BootId && status.HighWater == current.Ticket.RequestId && status.State == ActionState.Idle &&
        status.ReplyId == current.Ticket.RequestId && status.Reply == Reply.Released &&
        (status.Control == 0 && status.ControlAck == 0 || status.Control == 2 && status.ControlAck == 2);
    private static void RequireSaved(ApplicationSnapshot application, Binding current)
    {
        StationSnapshot s = application.Host.Station;
        RecordedCycle? recorded = application.Ledger.Cycles.SingleOrDefault(c => c.Session == current.Command.Key.Session && c.Cycle == current.Command.Key.Cycle);
        if (application.Busy || application.CloseRequested || application.Closed || application.Host.TaskActive || s.Pending is not null ||
            s.Session != current.Command.Key.Session || s.Cycle != current.Command.Key.Cycle || s.Order != current.Command.Order ||
            s.Terminal is null || !s.TerminalConsumed || recorded?.Terminal != s.Terminal || current.Terminal is null)
            throw new InvalidOperationException("Save/consume matching station terminal and settle task/pending before remote release.");
    }
    private StationReply? Map(Observation observed)
    {
        Binding current = Current!;
        if (observed.Resolution != Resolution.Known || observed.Snapshot is not { Terminal: true } status ||
            status.BootId != current.Ticket.BootId || status.Current != current.Ticket) return null;
        StationReply terminal = new(current.Command.Key, current.Command.Order.Workpiece, current.Command.Order.RecipeVersion,
            status.State == ActionState.Done, null, null);
        if (current.Terminal is not null && current.Terminal != terminal) throw new InvalidDataException("Remote terminal changed.");
        lock (state) binding = current with { Terminal = terminal };
        return terminal;
    }
    private Binding? Current { get { lock (state) return binding; } }
    private static void Validate(StationCommand command)
    {
        if (command.Key.Session == Guid.Empty || command.Key.Cycle <= 0 || command.Key.Operation <= 0 || !Enum.IsDefined(command.Key.Step) ||
            !StationApplication.Recipes.Any(r => r.Version == command.Order.RecipeVersion) ||
            command.Key.Step == StepKind.Move && (command.FrameId is not null || command.Judgment is not null))
            throw new ArgumentException("Invalid station command or fixed recipe.");
    }
    private void Note(Observation value) => SetResolution(value.Resolution + ":" + value.Snapshot?.State);
    private void SetResolution(string value) { lock (state) resolution = value; }
    private Task<T> Exclusive<T>(Func<Task<T>> work)
    {
        if (!calls.Wait(0)) return Task.FromException<T>(new InvalidOperationException("PortBusy: no concurrent I/O or hidden queue."));
        return Run();
        async Task<T> Run()
        {
            try { ObjectDisposedException.ThrowIf(disposed, this); return await work().ConfigureAwait(false); }
            finally { calls.Release(); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (!calls.Wait(0)) throw new InvalidOperationException("Cannot dispose during actual I/O.");
        try
        {
            if (disposed) return;
            await simulation.DisposeAsync().ConfigureAwait(false); await controller.DisposeAsync().ConfigureAwait(false);
            lock (state) disposed = true;
        }
        finally { calls.Release(); }
    }
}
