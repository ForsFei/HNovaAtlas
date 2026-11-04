namespace IndustrialStation;

internal static class LedgerSimulation
{
    internal static RestartReview DeclaredReview() => new("offline-test", "simulated-observation-only", DateTimeOffset.UnixEpoch.AddDays(1),
        true, true, true, true, PeerResultDisposition.AcceptedByPeer);
    // Deliberately separate from StationHost: this batch demonstrates durable ordering, not an automatic SDK adapter.
    internal static StationTerminal RunNormal(StationLedger ledger, WorkOrder order, Action? beforeDispatch = null)
    {
        if (!ledger.Report.CanStartNewCycle) throw new InvalidOperationException("RestartReviewRequired");
        StationController station = new(order.Producer);
        if (station.TryStart(order, OfflineSimulation.Ready) != Admission.Accepted) throw new InvalidOperationException("Not accepted.");
        StationSnapshot start = station.Snapshot;
        if (!ledger.Begin(start.Session, start.Cycle, order)) throw new InvalidOperationException("Intent already exists; never replay it."); // No command taken yet.
        beforeDispatch?.Invoke();
        OfflineSimulation.Finish(station, Verdict.Ng); // Four purely simulated commands.
        StationTerminal terminal = station.Snapshot.Terminal!;
        ledger.SaveTerminal(terminal); // Failure aborts before local consumption.
        if (!station.ConsumeTerminal(terminal)) throw new InvalidOperationException("Local consume failed.");
        return terminal;
    }
    public static void Demo()
    {
        string directory = Path.Combine(Path.GetTempPath(), "hnova-station-ledger-" + Guid.NewGuid().ToString("N"));
        Guid producer = Guid.NewGuid(); StationTerminal terminal;
        Console.WriteLine("OFFLINE FILES ONLY: no device, no replay, no physical recovery. Test files retained at: " + directory);
        using (StationLedger ledger = new(directory))
        {
            terminal = RunNormal(ledger, new(producer, 1, "part-ng", "recipe-v1"));
            Console.WriteLine($"Saved terminal: {terminal.Outcome}/{terminal.Judgment}; newCycleAllowed={ledger.Report.CanStartNewCycle}");
        }
        using (StationLedger reopened = new(directory))
        {
            Console.WriteLine($"Reopened: terminalPresent={reopened.Report.Cycles[0].Terminal is not null}; blocked={!reopened.Report.CanStartNewCycle}");
            reopened.Review(terminal.Session, terminal.Cycle, DeclaredReview());
            Console.WriteLine($"Explicit simulated review: allowed={reopened.Report.CanStartNewCycle}; no command executed by review");
            reopened.Begin(Guid.NewGuid(), 1, new(producer, 2, "part-interrupted", "recipe-v1"));
        }
        using StationLedger interrupted = new(directory);
        RecordedCycle pending = interrupted.Report.Cycles.Last();
        Console.WriteLine($"Interrupted intent: terminalPresent={pending.Terminal is not null}; blocked={!interrupted.Report.CanStartNewCycle}; autoReplay=false");
    }
}
