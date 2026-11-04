namespace IndustrialStation;

internal static class HostChecks
{
    private static int count;
    private static WorkOrder Order(StationHost h, long n = 1) => new(h.Producer, n, $"async-part-{n}", "recipe-v1");
    private static TaskCompletionSource<T> Gate<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public static async Task RunAsync()
    {
        count = 0;
        await NormalAsync(); await LateCloseAsync(); await UncertainAsync(); await CancellationAndWrongReplyAsync();
        await AutomaticTicksAsync(); await BlockingPrefixAsync(); await MailboxAsync(); await CleanupFailureAsync();
        Console.WriteLine($"PASS {count} gated async-host/port/timeout/reconciliation/close checks. Simulated ports only, no Sleep or real SDK.");
    }
    private static async Task NormalAsync()
    {
        ScriptedStationPort port = new(c => Task.FromResult<StationReply?>(OfflineSimulation.Reply(c, Verdict.Ng)));
        StationHost host = new(Guid.NewGuid(), port, new ManualStationClock(), automaticTicks: false);
        WorkOrder order = Order(host);
        Check(await host.StartAsync(order, OfflineSimulation.Ready) == Admission.Accepted, "host start accepted");
        HostSnapshot done = await AsyncSimulation.Until(host, s => s.Station.Stage == StationStage.Completed);
        Check(done.Station.Terminal is { Outcome: CycleOutcome.Completed, Judgment: Verdict.Ng }, "normal async NG cycle");
        Check(port.ExecuteCalls == 4 && port.ObserveCalls == 0 && port.Maximum == 1, "four sequential port actions, no query or overlap");
        Check(!done.TaskActive && !done.NeedsReconciliation, "actual task observed before terminal snapshot");
        Check(await host.StartAsync(order, OfflineSimulation.Ready) == Admission.AlreadyTracked, "duplicate request no rerun");
        Check(!await host.ReleaseAsync(AsyncSimulation.Recovery(done)), "local terminal not yet consumed");
        Check(!await host.ConsumeAsync(done.Station.Terminal! with { }), "cloned terminal refused");
        Check(await host.ConsumeAsync(done.Station.Terminal!), "original terminal consumed");
        Check(await host.ReleaseAsync(AsyncSimulation.Recovery(host.Snapshot)), "normal release");
        Check(await host.StartAsync(order, OfflineSimulation.Ready) == Admission.Stale, "old request not repeated");
        host.RequestClose(); await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Check(host.Snapshot.Station.Stage == StationStage.Closed && host.Snapshot.PortDisposed && port.DisposeCalls == 1, "idle host close disposed once");
        Check(await host.StartAsync(Order(host, 2), OfflineSimulation.Ready) == Admission.Closed, "closed host start cutoff");
    }
    private static async Task LateCloseAsync()
    {
        ManualStationClock clock = new(); TaskCompletionSource<StationCommand> entered = Gate<StationCommand>();
        TaskCompletionSource<StationReply?> exit = Gate<StationReply?>();
        ScriptedStationPort port = new(c => { entered.TrySetResult(c); return exit.Task; });
        StationHost host = new(Guid.NewGuid(), port, clock, automaticTicks: false);
        await host.StartAsync(Order(host), OfflineSimulation.Ready); StationCommand command = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await AsyncSimulation.Until(host, s => s.TaskActive);
        clock.Advance(TimeSpan.FromSeconds(2)); await host.PulseAsync(); HostSnapshot timed = host.Snapshot;
        Check(timed.Station.Terminal?.Reason == "TimedOut:Move" && timed.TaskActive, "logical deadline does not end task");
        Check(!await host.ReconcileAsync(), "no concurrent read-only query while original call active");
        Check(!await host.ResetAsync(AsyncSimulation.Recovery(timed)), "all-true declarations cannot bypass running task");
        host.RequestClose(); HostSnapshot closing = await AsyncSimulation.Until(host, s => s.Station.Stage == StationStage.Closing);
        Check(!host.Completion.IsCompleted && port.DisposeCalls == 0, "close never disposes underneath task");
        Check(await host.ConsumeAsync(closing.Station.Terminal!), "consume original timeout while closing");
        Check(!host.Completion.IsCompleted, "consumption alone cannot finish close");
        exit.SetResult(OfflineSimulation.Reply(command)); await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Check(host.Snapshot.LastObservation?.Disposition == ReplyDisposition.DrainedLate, "late result only drains");
        Check(host.Snapshot.Station.Terminal?.Outcome == CycleOutcome.Faulted && port.ExecuteCalls == 1, "no late capture or terminal rewrite");
        Check(port.Active == 0 && port.DisposeCalls == 1 && host.Snapshot.PortDisposed, "port disposed after real task exit");
    }
    private static async Task UncertainAsync()
    {
        StationCommand? original = null; int queries = 0;
        ScriptedStationPort port = new(c => { original = c; return Task.FromException<StationReply?>(new IOException("Injected transport ambiguity")); },
            c => Task.FromResult<StationReply?>(Interlocked.Increment(ref queries) == 1 ? null : OfflineSimulation.Reply(c)));
        StationHost host = new(Guid.NewGuid(), port, new ManualStationClock(), automaticTicks: false);
        await host.StartAsync(Order(host), OfflineSimulation.Ready);
        HostSnapshot unknown = await AsyncSimulation.Until(host, s => s.NeedsReconciliation);
        Check(unknown.Station.Stage == StationStage.Faulted && unknown.Station.Pending?.Key == original!.Key, "task fault leaves physical obligation");
        Check(unknown.LastObservation?.Result == "TaskFault:IOException" && !unknown.TaskActive, "task exception observed separately");
        Check(!await host.ResetAsync(AsyncSimulation.Recovery(unknown)), "task ended not enough to reset");
        Check(await host.ReconcileAsync(), "explicit read-only observation accepted");
        HostSnapshot still = await AsyncSimulation.Until(host, s => s.NeedsReconciliation && s.LastObservation is { ReadOnlyQuery: true, Result: "NoTerminal" });
        Check(still.Station.Pending is not null && port.ExecuteCalls == 1, "no terminal means no execute retry");
        host.RequestClose(); await AsyncSimulation.Until(host, s => s.Station.Stage == StationStage.Closing);
        Check(!host.Completion.IsCompleted && port.DisposeCalls == 0, "unknown physical state keeps close pending");
        Check(await host.ReconcileAsync(), "read-only reconciliation allowed during close");
        HostSnapshot drained = await AsyncSimulation.Until(host, s => !s.TaskActive && s.Station.Pending is null);
        Check(drained.Station.Terminal?.Outcome == CycleOutcome.Faulted && port.ObserveCalls == 2 && port.Maximum == 1, "explicit final evidence settles without overlap");
        Check(await host.ConsumeAsync(drained.Station.Terminal!), "consume retained fault");
        await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Check(port.ExecuteCalls == 1 && port.DisposeCalls == 1, "query did not resend physical intent");
    }
    private static async Task CancellationAndWrongReplyAsync()
    {
        foreach (bool cancel in new[] { true, false })
        {
            ScriptedStationPort port = new(c => cancel ? Task.FromCanceled<StationReply?>(new CancellationToken(true)) :
                Task.FromResult<StationReply?>(OfflineSimulation.Reply(c) with { RecipeVersion = "foreign" }),
                c => Task.FromResult<StationReply?>(OfflineSimulation.Reply(c)));
            StationHost host = new(Guid.NewGuid(), port, new ManualStationClock(), automaticTicks: false);
            await host.StartAsync(Order(host), OfflineSimulation.Ready);
            HostSnapshot unknown = await AsyncSimulation.Until(host, s => s.NeedsReconciliation);
            Check(unknown.Station.Stage == StationStage.Faulted && unknown.Station.Pending is not null, "cancel/wrong reply not synthetic failure terminal");
            Check(unknown.LastObservation?.Result == (cancel ? "TaskCanceled:TaskCanceledException" : "Returned"), "cancel/wrong reply diagnostic");
            await host.ConsumeAsync(unknown.Station.Terminal!); await host.ReconcileAsync();
            HostSnapshot settled = await AsyncSimulation.Until(host, s => !s.TaskActive && s.Station.Pending is null);
            Check(await host.ResetAsync(AsyncSimulation.Recovery(settled)) && port.ExecuteCalls == 1, "explicit reconciliation enables logical reset");
            host.RequestClose(); await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
    private static async Task AutomaticTicksAsync()
    {
        ManualStationClock clock = new(); TaskCompletionSource<StationCommand> entered = Gate<StationCommand>();
        TaskCompletionSource<StationReply?> exit = Gate<StationReply?>();
        ScriptedStationPort port = new(c => { entered.TrySetResult(c); return exit.Task; });
        StationHost host = new(Guid.NewGuid(), port, clock, automaticTicks: true);
        await host.StartAsync(Order(host), OfflineSimulation.Ready); StationCommand command = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(2)); // Timer drives Tick; test does NOT send PulseAsync.
        HostSnapshot timed = await AsyncSimulation.Until(host, s => s.Station.Stage == StationStage.Faulted);
        Check(timed.TaskActive && timed.Station.Terminal?.Reason == "TimedOut:Move", "automatic timer observes monotonic expiry");
        host.RequestClose(); await host.ConsumeAsync(timed.Station.Terminal!); exit.SetResult(OfflineSimulation.Reply(command));
        await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Check(host.Snapshot.PortDisposed && port.DisposeCalls == 1, "automatic timer stops during cleanup");
    }
    private static async Task BlockingPrefixAsync()
    {
        using ManualResetEventSlim release = new(); TaskCompletionSource<bool> entered = Gate<bool>(); ManualStationClock clock = new();
        ScriptedStationPort port = new(c =>
        {
            entered.TrySetResult(true);
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test port-prefix gate.");
            return Task.FromResult<StationReply?>(OfflineSimulation.Reply(c));
        });
        StationHost host = new(Guid.NewGuid(), port, clock, automaticTicks: false);
        await host.StartAsync(Order(host), OfflineSimulation.Ready); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(2)); await host.PulseAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Check(host.Snapshot.TaskActive && host.Snapshot.Station.Stage == StationStage.Faulted, "blocking prefix cannot block state-owner pulse");
        host.RequestClose(); await host.ConsumeAsync(host.Snapshot.Station.Terminal!); release.Set();
        await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Check(port.ExecuteCalls == 1 && port.DisposeCalls == 1, "blocking prefix drains without overlap or retry");
    }
    private sealed class BlockingClock : TimeProvider, IDisposable
    {
        public TaskCompletionSource<bool> Entered { get; } = Gate<bool>();
        private readonly ManualResetEventSlim release = new();
        public override long GetTimestamp()
        { Entered.TrySetResult(true); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test-only clock gate."); return 0; }
        public void Release() => release.Set();
        public void Dispose() { release.Set(); release.Dispose(); }
    }
    private static async Task MailboxAsync()
    {
        using BlockingClock clock = new();
        ScriptedStationPort port = new(c => Task.FromResult<StationReply?>(OfflineSimulation.Reply(c)));
        StationHost host = new(Guid.NewGuid(), port, clock, automaticTicks: false, mailboxCapacity: 1);
        Task<Admission> start = host.StartAsync(Order(host), OfflineSimulation.Ready);
        await clock.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task queued = host.PulseAsync(), rejected = host.PulseAsync();
        Check(await Fails(rejected, "MailboxFull"), "bounded mailbox rejects immediately, no hidden writer wait");
        host.RequestClose();
        Check(await host.StartAsync(Order(host, 2), OfflineSimulation.Ready) == Admission.Closed, "close signal independent of full mailbox");
        clock.Release(); Check(await start == Admission.Accepted, "already-accepted reducer call has explicit outcome"); await queued;
        HostSnapshot closing = await AsyncSimulation.Until(host, s => s.Station.Stage == StationStage.Closing);
        Check(port.ExecuteCalls == 0 && closing.Station.Pending is null, "close cutoff withdraws untransferred command");
        await host.ConsumeAsync(closing.Station.Terminal!); await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Check(port.DisposeCalls == 1, "accepted mailbox requests settled before cleanup");
    }
    private static async Task CleanupFailureAsync()
    {
        ScriptedStationPort port = new(c => Task.FromResult<StationReply?>(OfflineSimulation.Reply(c)), failDispose: true);
        StationHost host = new(Guid.NewGuid(), port, automaticTicks: false); host.RequestClose();
        Check(await Fails(host.Completion, "Host failed"), "cleanup error reaches completion");
        Check(host.Snapshot.Station.Stage == StationStage.Closed && !host.Snapshot.PortDisposed && port.DisposeCalls == 1, "logical closed not false disposal success");
    }
    private static async Task<bool> Fails(Task task, string text)
    { try { await task.WaitAsync(TimeSpan.FromSeconds(10)); return false; } catch (InvalidOperationException error) { return error.Message.Contains(text, StringComparison.Ordinal); } }
    private static void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException($"Async host check failed: {name}"); count++; }
}
