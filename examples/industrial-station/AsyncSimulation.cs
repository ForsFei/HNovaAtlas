namespace IndustrialStation;

internal sealed class ScriptedStationPort(Func<StationCommand, Task<StationReply?>> execute,
    Func<StationCommand, Task<StationReply?>>? observe = null, bool failDispose = false) : IStationAsyncPort
{
    private int active, maximum, executeCalls, observeCalls, disposeCalls;
    public int Active => Volatile.Read(ref active);
    public int Maximum => Volatile.Read(ref maximum);
    public int ExecuteCalls => Volatile.Read(ref executeCalls);
    public int ObserveCalls => Volatile.Read(ref observeCalls);
    public int DisposeCalls => Volatile.Read(ref disposeCalls);
    public Task<StationReply?> ExecuteAsync(StationCommand command) { Interlocked.Increment(ref executeCalls); return Run(command, execute); }
    public Task<StationReply?> ObserveAsync(StationCommand command)
    { Interlocked.Increment(ref observeCalls); return Run(command, observe ?? (_ => Task.FromResult<StationReply?>(null))); }
    private async Task<StationReply?> Run(StationCommand command, Func<StationCommand, Task<StationReply?>> action)
    {
        int now = Interlocked.Increment(ref active), seen = Maximum;
        while (now > seen)
        {
            int prior = Interlocked.CompareExchange(ref maximum, now, seen);
            if (prior == seen) break;
            seen = prior;
        }
        try { return await action(command).ConfigureAwait(false); }
        finally { Interlocked.Decrement(ref active); }
    }
    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref disposeCalls);
        if (Active != 0) throw new InvalidOperationException("Disposed underneath an active call.");
        if (failDispose) throw new IOException("Injected cleanup failure.");
        return ValueTask.CompletedTask;
    }
}

internal static class AsyncSimulation
{
    public static RecoveryEvidence Recovery(HostSnapshot s) => new(s.Station.Session, s.Station.Cycle, s.Station.RecoveryVersion,
        true, true, true, true, true, true); // Explicit offline declaration only.
    public static async Task<HostSnapshot> Until(StationHost host, Func<HostSnapshot, bool> predicate)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        HostSnapshot s = host.Snapshot;
        while (!predicate(s))
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (host.Completion.IsCompleted)
            { await host.Completion.ConfigureAwait(false); throw new InvalidOperationException("Host completed before expected snapshot."); }
            s = await host.ObserveAfterAsync(s.Revision).WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        return s;
    }
    public static async Task DemoAsync()
    {
        Console.WriteLine("OFFLINE ASYNC PORT ONLY: no PLC/card/camera/model, no automatic retry.");
        ManualStationClock clock = new();
        TaskCompletionSource<StationReply?> exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<StationCommand> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedStationPort port = new(c => { entered.TrySetResult(c); return exit.Task; });
        StationHost host = new(Guid.NewGuid(), port, clock, automaticTicks: false);
        await host.StartAsync(new(host.Producer, 1, "async-part-001", "offline-v1"), OfflineSimulation.Ready);
        StationCommand command = await entered.Task;
        clock.Advance(TimeSpan.FromSeconds(2)); await host.PulseAsync();
        HostSnapshot timed = host.Snapshot;
        Console.WriteLine($"Deadline: {timed.Station.Stage}; taskActive={timed.TaskActive}; pending={timed.Station.Pending?.Key.Step}");
        await host.ConsumeAsync(timed.Station.Terminal!); host.RequestClose();
        await Until(host, s => s.Station.Stage == StationStage.Closing);
        Console.WriteLine($"Close waits: completion={host.Completion.IsCompleted}; disposed={port.DisposeCalls}");
        exit.SetResult(OfflineSimulation.Reply(command));
        await host.Completion;
        Console.WriteLine($"Late result: {host.Snapshot.LastObservation?.Disposition}; stage={host.Snapshot.Station.Stage}; executeCalls={port.ExecuteCalls}; disposed={port.DisposeCalls}");
    }
}
