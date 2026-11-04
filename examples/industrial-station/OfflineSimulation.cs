namespace IndustrialStation;

public sealed class ManualStationClock : TimeProvider
{
    private readonly object sync = new();
    private long timestamp;
    private DateTimeOffset wall = DateTimeOffset.UnixEpoch;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() { lock (sync) return timestamp; }
    public override DateTimeOffset GetUtcNow() { lock (sync) return wall; }
    public void Advance(TimeSpan time)
    {
        if (time < TimeSpan.Zero) throw new ArgumentException("Monotonic clock cannot move backwards.");
        lock (sync) { timestamp = checked(timestamp + time.Ticks); wall += time; }
    }
    public void ShiftWallClock(TimeSpan delta) { lock (sync) wall += delta; }
}

public static class OfflineSimulation
{
    public static StartEvidence Ready { get; } = new(true, true, true, true, true);
    public static RecoveryEvidence DeclaredRecovery(StationController station)
    {
        StationSnapshot s = station.Snapshot;
        return new(s.Session, s.Cycle, s.RecoveryVersion, true, true, true, true, true, true);
    }
    // Explicit test evidence, never an SDK call or a real physical observation.
    public static StationReply Reply(StationCommand command, Verdict verdict = Verdict.Ok, bool success = true) => new(command.Key,
        command.Order.Workpiece, command.Order.RecipeVersion, success,
        command.Key.Step == StepKind.Capture ? success ? Guid.NewGuid() : null : command.FrameId,
        command.Key.Step == StepKind.Inspect ? success ? verdict : null : command.Judgment);
    public static void Finish(StationController station, Verdict verdict)
    {
        for (int i = 0; i < 4; i++)
        {
            StationCommand? command = station.TryTakeCommand(); if (command is null) return;
            station.Receive(Reply(command, verdict));
        }
    }
    public static void Demo()
    {
        Console.WriteLine("OFFLINE DECLARATIONS ONLY: no PLC, motion, camera, model, WPF or safety control.");
        ManualStationClock clock = new(); StationController station = new(Guid.NewGuid(), clock);
        WorkOrder order = new(station.Producer, 1, "part-001", "offline-recipe-v1");
        Console.WriteLine($"Start={station.TryStart(order, Ready)}");
        Finish(station, Verdict.Ng);
        StationTerminal terminal = station.Snapshot.Terminal!;
        Console.WriteLine($"Cycle={terminal.Cycle} Outcome={terminal.Outcome} Judgment={terminal.Judgment}; NG is a completed simulated decision, not a process failure.");
        station.ConsumeTerminal(terminal); station.ReleaseCompletedCycle(DeclaredRecovery(station));
        station.TryStart(new(station.Producer, 2, "part-002", "offline-recipe-v1"), Ready);
        for (int i = 0; i < 3; i++) { StationCommand command = station.TryTakeCommand()!; station.Receive(Reply(command)); }
        StationCommand publication = station.TryTakeCommand()!;
        clock.Advance(TimeSpan.FromSeconds(1)); station.Tick();
        StationTerminal timeout = station.Snapshot.Terminal!;
        Console.WriteLine($"Timeout={timeout.Reason}; pending={station.Snapshot.Pending?.Key.Step}; reset={station.Reset(DeclaredRecovery(station))}");
        Console.WriteLine($"LateAck={station.Receive(Reply(publication))}; outcome-still={station.Snapshot.Terminal!.Outcome}");
        station.ConsumeTerminal(timeout);
        Console.WriteLine($"Declared reset after drain={station.Reset(DeclaredRecovery(station))}");
        station.RequestClose(); Console.WriteLine($"Close={station.Snapshot.Stage}");
        foreach (StationEvent item in station.Snapshot.Journal)
            Console.WriteLine($"{item.Sequence,2} cycle={item.Cycle} {item.Stage,-18} {item.Event,-19} {item.Reason}");
    }
}
