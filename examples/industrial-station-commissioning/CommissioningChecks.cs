using System.Text.Json;
using IndustrialStationCommissioning;
namespace IndustrialStationCommissioning;
internal static class CommissioningChecks
{
    private static int count;
    internal static AcceptanceBaseline Baseline() => new("offline-teaching-source-v1", "net10.0-managed-no-native-sdk", "no-device-simulation-v1",
        "station-identity-demo-v1", "demo-ok-v1/demo-ng-v1", "synthetic-fixture-only/not-a-field-quality-criteria");
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); count++; }
    private static bool Fails(Action f) { try { f(); return false; } catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException) { return true; } }
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "hnova-commissioning-checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        AcceptanceBook book = new(Baseline());
        Check(book.Snapshot.Cases.Count == 8 && book.Snapshot.MissingFieldEvidence == 8, "initial book declares every field trial missing");
        Check(book.Snapshot.Cases.All(c => c.Field is null && c.Simulation is null), "no fabricated initial observations");
        TrialObservation sample = new("communication", TrialStage.Simulation, TrialOutcome.Passed, book.BaselineSha256, "offline-test",
            new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero), "synthetic-format-fixture-only");
        Check(book.Record(sample) && !book.Record(sample), "identical observation idempotent");
        Check(book.Snapshot.MissingFieldEvidence == 8 && book.Snapshot.Cases.Single(c => c.Case.Id == "communication").Field is null, "simulation pass cannot fill field evidence");
        Check(Fails(() => book.Record(sample with { EvidenceReference = "" })), "evidence reference required");
        Check(Fails(() => book.Record(sample with { Operator = " " })), "operator declaration required");
        Check(Fails(() => book.Record(sample with { ObservedUtc = default })), "observation time required");
        Check(Fails(() => book.Record(sample with { ObservedUtc = sample.ObservedUtc.ToOffset(TimeSpan.FromHours(8)) })), "UTC required for record time");
        Check(Fails(() => book.Record(sample with { BaselineSha256 = new string('0', 64) })), "different baseline refused");
        Check(Fails(() => book.Record(sample with { CaseId = "unknown" })), "unknown case refused");
        Check(Fails(() => book.Record(sample with { Stage = (TrialStage)99 })), "invalid stage refused");
        Check(Fails(() => book.Record(sample with { Outcome = (TrialOutcome)99 })), "invalid outcome refused");
        Check(Fails(() => book.Record(sample with { Outcome = TrialOutcome.NotRun })), "not-run remains absent rather than fake observation");
        Check(Fails(() => book.Record(sample with { Outcome = TrialOutcome.Failed })), "sealed result cannot be overwritten by different outcome");
        Check(Fails(() => book.Record(sample with { EvidenceReference = "different-trial" })), "sealed evidence cannot silently change");
        Check(new AcceptanceBook(Baseline()).BaselineSha256 == book.BaselineSha256, "identical declared baseline deterministic fingerprint");
        foreach (AcceptanceBaseline changed in new[] { Baseline() with { ApplicationRevision = "v2" }, Baseline() with { RuntimeAndSdkInventory = "sdk2" },
            Baseline() with { DeviceAndConfigurationRevision = "config2" }, Baseline() with { ProtocolRevision = "protocol2" },
            Baseline() with { RecipeRevision = "recipe2" }, Baseline() with { SampleAndCriteriaRevision = "samples2" } })
            Check(new AcceptanceBook(changed).BaselineSha256 != book.BaselineSha256, "baseline change requires a new book and evidence");
        Check(Fails(() => new AcceptanceBook(Baseline() with { ApplicationRevision = "" })), "empty baseline refused");
        Check(Fails(() => new AcceptanceBook(Baseline() with { RuntimeAndSdkInventory = "a\nb" })), "control characters refused");
        // Synthetic format checks only; never represented as actual field execution in demo or deployment.
        AcceptanceBook synthetic = new(Baseline()); synthetic.Record(sample with { Stage = TrialStage.Field, Outcome = TrialOutcome.Failed });
        Check(synthetic.Snapshot.FailedFieldEvidence == 1 && synthetic.Snapshot.MissingFieldEvidence == 7, "failed field declaration remains failed and missing cases stay missing");
        AcceptanceReport savedSnapshot = synthetic.Snapshot;
        synthetic.Record(sample with { CaseId = "persistence", Stage = TrialStage.Field, Outcome = TrialOutcome.Failed });
        Check(savedSnapshot.MissingFieldEvidence == 7 && synthetic.Snapshot.MissingFieldEvidence == 6 && savedSnapshot.Cases.Single(c => c.Case.Id == "persistence").Field is null, "snapshots do not gain later observations");
        IReadOnlyList<DrillResult> drills = OfflineDrills.Run(root);
        foreach (DrillResult drill in drills) Check(drill.Passed, drill.Name);
        Check(drills.Count == 3 && drills.Select(d => d.CaseId).Distinct().Count() == 3, "three explicitly scoped offline drills");
        string path = book.WriteNewReport(Path.Combine(root, "report"));
        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(path));
        Check(json.RootElement.GetProperty("MissingFieldEvidence").GetInt32() == 8 && json.RootElement.GetProperty("Scope").GetString() == "RecordOnly-NoMachinePermission", "persisted report does not claim hardware readiness");
        byte[] before = File.ReadAllBytes(path); Check(Fails(() => book.WriteNewReport(Path.GetDirectoryName(path)!)), "published report not overwritten");
        Check(File.ReadAllBytes(path).SequenceEqual(before), "refused overwrite preserves original content");
        string partial = Path.Combine(root, "partial"); Directory.CreateDirectory(partial); File.WriteAllText(Path.Combine(partial, "acceptance-report.json.pending"), "injected-incomplete");
        Check(Fails(() => book.WriteNewReport(partial)) && File.ReadAllText(Path.Combine(partial, "acceptance-report.json.pending")) == "injected-incomplete", "pending report preserved and blocks publish");
        Console.WriteLine($"PASS {count} commissioning record/baseline/offline-drill checks; files retained at {root}");
    }
    public static void Demo()
    {
        string root = Path.Combine(Path.GetTempPath(), "hnova-commissioning-demo-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        AcceptanceBook book = new(Baseline());
        foreach (DrillResult drill in OfflineDrills.Run(root))
        {
            book.Record(new(drill.CaseId, TrialStage.Simulation, drill.Passed ? TrialOutcome.Passed : TrialOutcome.Failed, book.BaselineSha256,
                "offline-demo", DateTimeOffset.UtcNow, drill.Name + ": " + drill.Detail));
            Console.WriteLine($"SIMULATION {drill.Name}: {drill.Passed}; {drill.Detail}");
        }
        string path = book.WriteNewReport(root);
        Console.WriteLine($"Missing field evidence: {book.Snapshot.MissingFieldEvidence}/8; no hardware executed; no machine permission. Report: {path}");
    }
}
