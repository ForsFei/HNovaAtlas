using System.Text.Json.Nodes;

namespace IndustrialStation;

internal static class LedgerChecks
{
    private static int count;
    private static string root = "";
    public static void Run()
    {
        root = Path.Combine(Path.GetTempPath(), "hnova-station-ledger-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        NormalAndRestart(); Interrupted(); WriteFailures(); LostTerminalResponse(); Corruption();
        Console.WriteLine($"PASS {count} persistence/reopen checks; temporary evidence retained at {root}");
    }
    private static string NewDirectory() => Path.Combine(root, Guid.NewGuid().ToString("N"));
    private static WorkOrder Order(long n = 1, Guid? producer = null) => new(producer ?? Guid.NewGuid(), n, "part-001", "recipe-v1");
    private static void NormalAndRestart()
    {
        string dir = NewDirectory(); WorkOrder order = Order(); Guid session = Guid.NewGuid();
        StationTerminal terminal = new(session, 1, order, CycleOutcome.Completed, "ResultAcknowledged", Guid.NewGuid(), Verdict.Ng);
        using (StationLedger ledger = new(dir))
        {
            Check(ledger.Report.CanStartNewCycle && ledger.Report.Cycles.Count == 0, "empty new log ready");
            Check(ledger.Begin(session, 1, order), "begin saved");
            Check(!ledger.Begin(session, 1, order with { }), "same begin structural idempotence");
            Check(!ledger.Report.CanStartNewCycle, "intent blocks next cycle");
            Check(Fails(() => ledger.Begin(session, 1, order with { Workpiece = "other" })), "identity conflict");
            Check(Fails(() => ledger.Begin(Guid.NewGuid(), 1, Order())), "unreviewed rejects new intent");
            Check(Fails(() => { using StationLedger competing = new(dir); }), "exclusive cooperating writer");
            Check(ledger.SaveTerminal(terminal), "NG terminal persisted");
            Check(!ledger.SaveTerminal(terminal with { }), "terminal structural idempotence after deserialization semantics");
            Check(Fails(() => ledger.SaveTerminal(terminal with { Judgment = Verdict.Ok })), "conflicting terminal rejected");
            Check(!ledger.Report.CanStartNewCycle, "saved terminal is not review");
            Check(Directory.GetFiles(dir, "*.json").Length == 2 && Directory.GetFiles(dir, "*.tmp").Length == 0, "two published files no temp");
        }
        using (StationLedger ledger = new(dir))
        {
            Check(ledger.Report.Cycles.Single().Terminal == terminal, "reopened full terminal roundtrip");
            Check(!ledger.Report.CanStartNewCycle, "restart remains blocked");
            Check(Fails(() => ledger.Review(session, 1, LedgerSimulation.DeclaredReview() with { MotionIdle = false })), "incomplete evidence rejected");
            Check(Fails(() => ledger.Review(session, 1, LedgerSimulation.DeclaredReview() with { ResultDisposition = (PeerResultDisposition)99 })), "unknown disposition rejected");
            RestartReview review = LedgerSimulation.DeclaredReview();
            Check(ledger.Review(session, 1, review), "explicit review saved");
            Check(!ledger.Review(session, 1, review with { }), "same review idempotent");
            Check(Fails(() => ledger.Review(session, 1, review with { EvidenceReference = "different" })), "sealed review cannot overwrite");
            Check(ledger.Report.CanStartNewCycle, "review permits new intent only");
            Check(Fails(() => ledger.Begin(Guid.NewGuid(), 1, order)), "producer high water survives restart");
        }
        using (StationLedger ledger = new(dir))
        {
            Check(ledger.Report.CanStartNewCycle, "review roundtrip");
            Check(ledger.Begin(Guid.NewGuid(), 1, order with { Number = 2 }), "higher producer number accepted");
        }
        StationLedger closed = new(NewDirectory()); closed.Dispose();
        Check(Fails(() => _ = closed.Report), "disposed access rejected");
    }
    private static void Interrupted()
    {
        string dir = NewDirectory(); Guid session = Guid.NewGuid(); WorkOrder order = Order();
        using (StationLedger ledger = new(dir)) ledger.Begin(session, 1, order);
        using (StationLedger ledger = new(dir))
        {
            Check(!ledger.Report.CanStartNewCycle && ledger.Report.Cycles[0].Terminal is null, "intent-only restart unknown");
            int dispatched = 0;
            Check(Fails(() => LedgerSimulation.RunNormal(ledger, Order(), () => dispatched++)) && dispatched == 0, "restart gate prevents simulated dispatch");
            Check(Fails(() => ledger.SaveTerminal(new(session, 1, order with { RecipeVersion = "wrong" }, CycleOutcome.Faulted, "unknown", null, null))), "terminal order mismatch");
            Check(Fails(() => ledger.SaveTerminal(new(session, 1, order, CycleOutcome.Completed, "bad", null, null))), "completed requires frame verdict");
            Check(ledger.Review(session, 1, LedgerSimulation.DeclaredReview() with { ResultDisposition = PeerResultDisposition.ConfirmedNotPublished }), "unknown cycle can be explicitly reconciled without invented terminal");
            Check(ledger.Report.Cycles[0].Terminal is null && ledger.Report.CanStartNewCycle, "review preserves missing terminal");
            Check(Fails(() => ledger.SaveTerminal(new(session, 1, order, CycleOutcome.Faulted, "late", null, null))), "reviewed missing terminal not rewritten");
        }
        string normal = NewDirectory();
        using (StationLedger ledger = new(normal))
        {
            int dispatchMarkers = 0;
            StationTerminal terminal = LedgerSimulation.RunNormal(ledger, Order(), () =>
            {
                dispatchMarkers++;
                Check(Directory.GetFiles(normal, "*.json").Length == 1 && ledger.Report.Cycles[0].Terminal is null, "intent published before first simulation");
            });
            Check(dispatchMarkers == 1 && terminal.Judgment == Verdict.Ng && ledger.Report.Cycles[0].Terminal == terminal, "ordered normal simulation persisted");
        }
    }
    private static void WriteFailures()
    {
        foreach (CommitPoint point in Enum.GetValues<CommitPoint>())
        {
            string dir = NewDirectory(); int dispatches = 0;
            using (StationLedger ledger = new(dir, p => { if (p == point) throw new IOException("injected boundary"); }))
            {
                Check(Fails(() => LedgerSimulation.RunNormal(ledger, Order(), () => dispatches++)) && dispatches == 0, $"{point} failure prevents dispatch");
                Check(!ledger.Report.CanStartNewCycle, $"{point} poisons admission");
                Check(Fails(() => ledger.Begin(Guid.NewGuid(), 1, Order())), $"{point} rejects further writes");
            }
            if (point == CommitPoint.BeforePublish)
                Check(Fails(() => { using StationLedger reopened = new(dir); }), "leftover temp fails closed");
            else
            {
                using StationLedger reopened = new(dir);
                Check(reopened.Report.Cycles.Count == 1 && !reopened.Report.CanStartNewCycle, "published before lost response recovered as blocked intent");
            }
        }
        string resultDir = NewDirectory(); bool armed = false;
        using (StationLedger ledger = new(resultDir, p => { if (armed && p == CommitPoint.BeforePublish) throw new IOException("terminal write failed"); }))
        {
            StationController station = new(Guid.NewGuid());
            station.TryStart(new(station.Producer, 1, "part", "v1"), OfflineSimulation.Ready);
            ledger.Begin(station.Session, 1, station.Snapshot.Order!); OfflineSimulation.Finish(station, Verdict.Ok); armed = true;
            Check(Fails(() => ledger.SaveTerminal(station.Snapshot.Terminal!)) && !station.Snapshot.TerminalConsumed, "failed save leaves local terminal unconsumed");
        }
    }
    private static void LostTerminalResponse()
    {
        string dir = NewDirectory(); bool armed = false; Guid session = Guid.NewGuid(); WorkOrder order = Order();
        StationTerminal terminal = new(session, 1, order, CycleOutcome.Faulted, "DeclaredFailure", null, null);
        using (StationLedger ledger = new(dir, p => { if (armed && p == CommitPoint.AfterPublish) throw new IOException("lost return"); }))
        {
            ledger.Begin(session, 1, order); armed = true;
            Check(Fails(() => ledger.SaveTerminal(terminal)), "terminal published but caller sees failure");
            Check(!ledger.Report.CanStartNewCycle, "lost terminal response blocks caller");
        }
        using StationLedger reopened = new(dir);
        Check(reopened.Report.Cycles[0].Terminal == terminal && !reopened.SaveTerminal(terminal), "reopened published terminal idempotent, no duplicate record");
        Check(!reopened.Report.CanStartNewCycle && Directory.GetFiles(dir, "*.json").Length == 2, "saved result still needs explicit review");
    }
    private static void Corruption()
    {
        Action<JsonObject>[] mutations = [
            n => n["Schema"] = 2,
            n => n["Sequence"] = 9,
            n => n["Unexpected"] = true,
            n => n.Remove("Order"),
            n => n["Kind"] = "OtherKind",
            n => n["Session"] = Guid.Empty.ToString(),
            n => n["Order"]!["Number"] = 0
        ];
        foreach (Action<JsonObject> mutation in mutations)
        {
            string dir = NewDirectory(); using (StationLedger ledger = new(dir)) ledger.Begin(Guid.NewGuid(), 1, Order());
            string file = Path.Combine(dir, "000001.json"); JsonObject value = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            mutation(value); File.WriteAllText(file, value.ToJsonString());
            Check(Fails(() => { using StationLedger bad = new(dir); }), "invalid schema/field/identity refused");
        }
        foreach (string content in new[] { "{", "null", new string(' ', 65537) })
        {
            string dir = NewDirectory(); Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "000001.json"), content);
            Check(Fails(() => { using StationLedger bad = new(dir); }), "truncated/null/oversize/duplicate JSON refused");
        }
        string duplicate = NewDirectory(); using (StationLedger ledger = new(duplicate)) ledger.Begin(Guid.NewGuid(), 1, Order());
        string duplicateFile = Path.Combine(duplicate, "000001.json");
        string completeJson = File.ReadAllText(duplicateFile).Replace("\"Schema\": 1", "\"Schema\": 1, \"Schema\": 1", StringComparison.Ordinal);
        File.WriteAllText(duplicateFile, completeJson);
        Check(Fails(() => { using StationLedger bad = new(duplicate); }), "duplicate property in otherwise valid record refused");
        string gap = NewDirectory(); using (StationLedger ledger = new(gap)) ledger.Begin(Guid.NewGuid(), 1, Order());
        File.Move(Path.Combine(gap, "000001.json"), Path.Combine(gap, "000002.json"));
        Check(Fails(() => { using StationLedger bad = new(gap); }), "sequence gap refused");
        string unexpected = NewDirectory(); Directory.CreateDirectory(unexpected); File.WriteAllText(Path.Combine(unexpected, "notes.txt"), "not part of ledger");
        Check(Fails(() => { using StationLedger bad = new(unexpected); }), "unknown directory content refused");
    }
    private static bool Fails(Action action)
    {
        try { action(); return false; }
        catch (Exception e) when (e is InvalidDataException or IOException or InvalidOperationException or System.Text.Json.JsonException or ObjectDisposedException) { return true; }
    }
    private static void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException("Ledger check failed: " + name); count++; }
}
