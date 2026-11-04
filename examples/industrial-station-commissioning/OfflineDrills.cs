using IndustrialStation;
using IndustrialStationEvidence;
namespace IndustrialStationCommissioning;

public sealed record DrillResult(string CaseId, string Name, bool Passed, string Detail);
public static class OfflineDrills
{
    public static IReadOnlyList<DrillResult> Run(string diagnosticDirectory)
    {
        List<DrillResult> results = new();
        Guid producer = Guid.NewGuid(); ManualStationClock time = new(); StationController c = new(producer, time);
        WorkOrder order = new(producer, 1, "offline-drill", "demo-ok-v1");
        bool admitted = c.TryStart(order, new(true, true, true, true, true)) == Admission.Accepted;
        StationCommand original = c.TryTakeCommand() ?? throw new InvalidOperationException("No simulated command");
        time.Advance(TimeSpan.FromSeconds(2)); c.Tick(); StationTerminal? terminal = c.Snapshot.Terminal;
        bool late = c.Receive(new(original.Key, order.Workpiece, order.RecipeVersion, true, null, null)) == ReplyDisposition.DrainedLate;
        results.Add(new("communication", "late-move-reply", admitted && late && ReferenceEquals(c.Snapshot.Terminal, terminal) &&
            terminal?.Outcome == CycleOutcome.Faulted && c.Snapshot.Pending is null && c.TryTakeCommand() is null,
            "Reducer/model only: a late successful Move reply drains the original obligation without Capture or terminal rewrite"));
        StationController binding = new(producer, new ManualStationClock()); binding.TryStart(order, new(true, true, true, true, true));
        StationCommand move = binding.TryTakeCommand()!;
        bool wrong = binding.Receive(new(move.Key with { Session = Guid.NewGuid() }, order.Workpiece, order.RecipeVersion, true, null, null)) == ReplyDisposition.IgnoredCorrelation;
        results.Add(new("camera-correlation", "wrong-session-reply", wrong && binding.Snapshot.Pending == move && binding.TryTakeCommand() is null,
            "Generic station identity reducer only, not real camera trigger/frame mapping: wrong-session reply cannot advance or resend"));
        string absent = Path.Combine(diagnosticDirectory, "not-created-" + Guid.NewGuid().ToString("N"));
        ReconciliationReport report = EvidenceReconciler.Inspect(absent, absent, absent);
        results.Add(new("persistence", "missing-ledger-readonly", report.RequiresAttention && report.Rows.Count == 0 && report.StorageIssues.Count == 1 && !Directory.Exists(absent),
            "Actual local read attempt: missing ledger reports unavailable, creates no directory and issues no device action"));
        return results.AsReadOnly();
    }
}
