using System.Collections.ObjectModel;

namespace IndustrialStation;

public sealed record StationRecipe(string Version, Verdict Judgment, int SimulatedDelayMilliseconds)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Version) || Version.Length > 64 || Version != Version.Trim() || Version.Any(char.IsControl) ||
            !Enum.IsDefined(Judgment) || SimulatedDelayMilliseconds is < 0 or > 250) throw new ArgumentException("Invalid offline recipe.");
    }
}
public sealed record ApplicationSnapshot(HostSnapshot Host, LedgerReport Ledger, bool Busy, bool CloseRequested, bool Closed, string? Error);

// Application-level composition. All synchronous disk work stays on the one admitted background operation.
public sealed class StationApplication
{
    public static Guid DemoProducer { get; } = new("093694c1-6dca-4d0c-81ee-e7de9ff27757");
    public static IReadOnlyList<StationRecipe> Recipes { get; } = Array.AsReadOnly(new[]
    { new StationRecipe("demo-ok-v1", Verdict.Ok, 100), new StationRecipe("demo-ng-v1", Verdict.Ng, 100) });
    private readonly StationLedger ledger;
    private readonly StationHost host;
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly object state = new();
    private LedgerReport report;
    private bool busy, closing, closed;
    private string? error;
    private StationApplication(StationLedger ledger, IStationAsyncPort port, TimeProvider? clock)
    { this.ledger = ledger; report = ledger.Report; host = new(DemoProducer, port, clock); }
    public string DirectoryPath => ledger.DirectoryPath;
    internal Task<HostSnapshot> ObserveHostAsync(long revision) => host.ObserveAfterAsync(revision);
    public static Task<StationApplication> OpenAsync(string directory) => OpenCoreAsync(() => new(directory), new RecipeSimulationPort(Recipes), null);
    // Ownership transfers to the application, including disposal if opening the ledger fails.
    public static Task<StationApplication> OpenWithPortAsync(string directory, IStationAsyncPort port, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(port);
        return OpenCoreAsync(() => new(directory), port, clock);
    }
    internal static Task<StationApplication> OpenForChecksAsync(Func<StationLedger> factory, IStationAsyncPort port, TimeProvider? clock = null) => OpenCoreAsync(factory, port, clock);
    private static Task<StationApplication> OpenCoreAsync(Func<StationLedger> factory, IStationAsyncPort port, TimeProvider? clock) => Task.Run(async () =>
    {
        StationLedger? ledger = null;
        try { ledger = factory(); return new StationApplication(ledger, port, clock); }
        catch { ledger?.Dispose(); await port.DisposeAsync().ConfigureAwait(false); throw; }
    });
    public ApplicationSnapshot Snapshot
    {
        get
        {
            lock (state) return new(host.Snapshot, report, busy, closing, closed,
                error ?? (host.Completion.IsFaulted ? host.Completion.Exception!.GetBaseException().Message : null));
        }
    }
    public Task<Admission> StartAsync(string workpiece, string recipeVersion) => RunAsync(async () =>
    {
        if (Snapshot.CloseRequested) throw new InvalidOperationException("Closing");
        StationRecipe recipe = Recipes.SingleOrDefault(r => r.Version == recipeVersion) ?? throw new ArgumentException("Unknown recipe version.");
        recipe.Validate();
        if (string.IsNullOrWhiteSpace(workpiece) || workpiece.Length > 64 || workpiece != workpiece.Trim() || workpiece.Any(char.IsControl))
            throw new ArgumentException("Workpiece must be trimmed, nonempty, <=64 characters.");
        StationSnapshot before = host.Snapshot.Station;
        if (before.Stage != StationStage.Idle || !report.CanStartNewCycle) throw new InvalidOperationException("NotIdleOrReviewRequired");
        long number = checked(report.Cycles.Where(c => c.Order.Producer == DemoProducer).Select(c => c.Order.Number).DefaultIfEmpty(0).Max() + 1);
        WorkOrder order = new(DemoProducer, number, workpiece, recipe.Version);
        if (!ledger.Begin(before.Session, checked(before.Cycle + 1), order)) throw new InvalidOperationException("ExistingIntentNeverReplay");
        RefreshReport();
        // Only this application admits cycles. A concurrent close may now reject Start; the durable intent stays for review.
        if (Snapshot.CloseRequested) throw new InvalidOperationException("ClosedDuringIntentWrite: review saved intent on reopen.");
        Admission accepted = await host.StartAsync(order, OfflineSimulation.Ready).ConfigureAwait(false);
        if (accepted != Admission.Accepted) throw new InvalidOperationException("HostRejectedAfterIntent: " + accepted);
        return accepted;
    });
    public Task<bool> SaveResultAsync() => RunAsync(SaveResultCoreAsync);
    private async Task<bool> SaveResultCoreAsync()
    {
        StationSnapshot current = host.Snapshot.Station;
        if (current.Terminal is null) return false;
        ledger.SaveTerminal(current.Terminal); RefreshReport();
        if (current.TerminalConsumed) return true;
        return await host.ConsumeAsync(current.Terminal).ConfigureAwait(false);
    }
    public Task<bool> ReconcileAsync() => RunAsync(() => host.ReconcileAsync());
    public Task<bool> StopAsync() => RunAsync(async () =>
    {
        if (host.Snapshot.Station.Order is null) return false;
        await host.StopAsync().ConfigureAwait(false); return true;
    });
    public Task<bool> SimulateReviewAndReleaseAsync(PeerResultDisposition resultDisposition) => RunAsync(async () =>
    {
        if (Snapshot.CloseRequested) throw new InvalidOperationException("Closing: review the old cycle after reopen.");
        if (!Enum.IsDefined(resultDisposition)) throw new ArgumentException("Explicit peer disposition required.");
        HostSnapshot current = host.Snapshot;
        if (current.TaskActive || current.Station.Pending is not null) throw new InvalidOperationException("TaskOrPendingStillActive");
        if (current.Station.Terminal is not null && !current.Station.TerminalConsumed) throw new InvalidOperationException("SaveAndConsumeFirst");
        RecordedCycle cycle = report.Cycles.SingleOrDefault(c => c.Review is null) ?? throw new InvalidOperationException("NoCycleToReview");
        RestartReview review = new("offline-operator", "explicit-simulated-review-only", DateTimeOffset.UtcNow,
            true, true, true, true, resultDisposition);
        ledger.Review(cycle.Session, cycle.Cycle, review); RefreshReport();
        if (current.Station.Order is null) return true; // Old-session startup review; no old controller is restored.
        RecoveryEvidence recovery = new(current.Station.Session, current.Station.Cycle, current.Station.RecoveryVersion, true, true, true, true, true, true);
        bool released = current.Station.Stage == StationStage.Completed
            ? await host.ReleaseAsync(recovery).ConfigureAwait(false) : await host.ResetAsync(recovery).ConfigureAwait(false);
        if (!released) throw new InvalidOperationException("LocalReleaseRejected; no automatic retry.");
        return true;
    });
    public void RequestClose()
    {
        lock (state) { if (closed) return; closing = true; }
        host.RequestClose(); // Independent of the application/disk operation slot.
    }
    // One finite pump step, never holds the application slot while waiting for an unknown device task.
    public Task<bool> CloseStepAsync()
    {
        RequestClose();
        return RunAsync(async () =>
        {
            if (Snapshot.Closed) return true;
            if (host.Snapshot.Station.Terminal is not null) await SaveResultCoreAsync().ConfigureAwait(false);
            if (!host.Completion.IsCompleted) return false;
            await host.Completion.ConfigureAwait(false);
            ledger.Dispose();
            lock (state) closed = true;
            return true;
        }, allowClosed: true);
    }
    private Task<T> RunAsync<T>(Func<Task<T>> action, bool allowClosed = false)
    {
        if (!operation.Wait(0)) return Task.FromException<T>(new InvalidOperationException("ApplicationBusy: request not accepted."));
        lock (state)
        {
            if (closed && !allowClosed) { operation.Release(); return Task.FromException<T>(new InvalidOperationException("ApplicationClosed")); }
            busy = true; error = null;
        }
        return Task.Run(async () =>
        {
            try { return await action().ConfigureAwait(false); }
            catch (Exception failure)
            {
                lock (state) error = failure.Message;
                if (!Snapshot.Closed) RefreshReport();
                throw;
            }
            finally { lock (state) { busy = false; operation.Release(); } }
        });
    }
    private void RefreshReport() { LedgerReport next = ledger.Report; lock (state) report = next; }
}

public sealed class RecipeSimulationPort : IStationAsyncPort
{
    private readonly IReadOnlyDictionary<string, StationRecipe> recipes;
    private StationReply? last;
    private bool disposed;
    public RecipeSimulationPort(IEnumerable<StationRecipe> recipes)
    {
        StationRecipe[] values = recipes.ToArray(); foreach (StationRecipe value in values) value.Validate();
        this.recipes = new ReadOnlyDictionary<string, StationRecipe>(values.ToDictionary(r => r.Version, StringComparer.Ordinal));
    }
    public async Task<StationReply?> ExecuteAsync(StationCommand command)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        StationRecipe recipe = recipes.TryGetValue(command.Order.RecipeVersion, out StationRecipe? value) ? value : throw new ArgumentException("Unknown captured recipe.");
        await Task.Delay(recipe.SimulatedDelayMilliseconds).ConfigureAwait(false); // Presentation pacing only, no device timing.
        StationReply reply = OfflineSimulation.Reply(command, recipe.Judgment);
        last = reply; return reply;
    }
    public Task<StationReply?> ObserveAsync(StationCommand command)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return Task.FromResult(last?.Key == command.Key ? last : null); // Read-only, no Execute replay.
    }
    public ValueTask DisposeAsync() { disposed = true; return ValueTask.CompletedTask; }
}
