using System.Text.Json.Nodes;
using IndustrialStation;
using IndustrialStationIntegrated;
using IndustrialStationVision;
using IndustrialVision;
namespace IndustrialStationEvidence;

internal static class EvidenceChecks
{
    private static int count;
    private static string root = "";
    private static void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); count++; }
    private static bool Fails(Action action) { try { action(); return false; } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException) { return true; } }
    private static async Task<bool> FailsAsync(Func<Task> action) { try { await action(); return false; } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or ArgumentException) { return true; } }
    private static async Task Prepare(IntegratedStationSession s, IntegratedStationPort p)
    {
        s.OpenMotionSimulation(); s.EnableMotionSimulation(); s.OpenReplay(); Task<bool> reference = s.BeginReferenceSimulation();
        for (int i = 0; i < 1000 && !reference.IsCompleted; i++) p.AdvanceSimulation(.01);
        if (!await reference.WaitAsync(TimeSpan.FromSeconds(2))) throw new InvalidOperationException("Reference failed");
    }
    private static async Task Finish(IntegratedStationSession s, IntegratedStationPort p)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (s.Snapshot.Host.Station.Stage != StationStage.Completed || s.Snapshot.Host.TaskActive)
        { p.AdvanceSimulation(.01); await Task.Delay(1, timeout.Token); }
    }
    private static async Task Close(IntegratedStationSession s, IntegratedStationPort p)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10)); s.RequestClose();
        while (!await s.CloseStepAsync()) { p.TryAdvanceSimulation(.01); await Task.Delay(1, timeout.Token); }
    }
    public static async Task RunAsync()
    {
        root = Path.Combine(Path.GetTempPath(), "hnova-station-evidence-checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        await RoundTrip(); await WriteFailure(); await StopWithoutFrame();
        Console.WriteLine($"PASS {count} evidence identity/pixels/publication/failure/reopen checks; files retained at {root}");
    }
    private static async Task RoundTrip()
    {
        string ledger = Path.Combine(root, "ledger"), storage = Path.Combine(root, "evidence");
        IntegratedStationPort p = new(); IntegratedStationSession s = await IntegratedStationSession.OpenAsync(ledger, p, new ManualStationClock());
        using EvidenceStore store = new(storage); EvidenceWorkflow flow = new(s, p, store);
        Check(Fails(() => new EvidenceStore(storage)), "exclusive writer lock");
        Check(Fails(() => p.ExportEvidence(s.Snapshot)), "no invented evidence before preparation/cycle");
        await Prepare(s, p);
        EvidenceReceipt? first = null;
        for (int i = 1; i <= 2; i++)
        {
            await s.StartAsync("part:/\\-" + i, i == 1 ? "demo-ok-v1" : "demo-ng-v1");
            Check(Fails(() => p.ExportEvidence(s.Snapshot)), "no unfinished evidence");
            await Finish(s, p); VisionEvidence evidence = p.ExportEvidence(s.Snapshot);
            Check(evidence.Terminal.FrameId == evidence.FrameId && evidence.Inspection.Frame == evidence.Frame.Stamp, "export preserves original cycle/frame");
            EvidenceReceipt before = store.Save(evidence);
            Check(store.Save(evidence) == before && Directory.GetDirectories(storage).Length == i, "identical retry no extra directory");
            Check(!s.Snapshot.Host.Station.TerminalConsumed && !s.Devices.Vision.CycleAcknowledged, "evidence publication alone does not consume or acknowledge");
            Check(Fails(() => store.Save(evidence with { FrameId = Guid.NewGuid() })), "mismatched frame rejected");
            Check(Fails(() => store.Save(evidence with { Terminal = evidence.Terminal with { Judgment = i == 1 ? Verdict.Ng : Verdict.Ok } })), "contradictory verdict rejected");
            PixelStatistics wrong = evidence.Inspection.Measurement! with { Mean = 12 };
            Check(Fails(() => store.Save(evidence with { Inspection = evidence.Inspection with { Measurement = wrong } })), "wrong statistics rejected");
            Check(Fails(() => store.Save(evidence with { Terminal = evidence.Terminal with { Reason = "conflict" } })), "same identity changed metadata conflict");
            EvidenceReceipt saved = await flow.SaveThenAcknowledgeAsync();
            Check(saved == before && s.Snapshot.Host.Station.TerminalConsumed && s.Devices.Vision.RetainedResults == 0, "verified evidence before terminal save/consume and original ack");
            Check(Fails(() => p.ExportEvidence(s.Snapshot)), "acknowledged frame cannot be re-exported");
            Check(saved.Manifest.Inspection.Measurement!.Mean == (i == 1 ? 220 : 75) && saved.Manifest.Terminal.Judgment == (i == 1 ? Verdict.Ok : Verdict.Ng), "actual source pixel statistics preserved");
            await s.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer);
            first ??= saved;
        }
        Check(s.Devices.Motion.MoveCalls == 2 && s.Devices.Vision.FramesAcquired == 2 && s.Devices.Vision.MeasureCalls == 2, "save/retry/read never repeat motion/capture/inspect");
        await Close(s, p); store.Dispose();
        using EvidenceStore reopened = new(storage);
        Check(reopened.Read(first!.Manifest.Terminal.Session, 1) == first, "evidence reopens with no live devices");
        using StationLedger journal = new(ledger);
        Check(journal.Report.Cycles[0].Terminal == first.Manifest.Terminal && journal.Report.CanStartNewCycle, "evidence matches durable terminal");
        CorruptionCases(first);
    }
    private static void CorruptionCases(EvidenceReceipt original)
    {
        void Case(string label, Action<string> corrupt)
        {
            string directory = Path.Combine(root, label), bundle = Path.Combine(directory, Path.GetFileName(original.DirectoryPath));
            Directory.CreateDirectory(bundle);
            foreach (string name in new[] { "image.pgm", "manifest.json" }) File.Copy(Path.Combine(original.DirectoryPath, name), Path.Combine(bundle, name));
            corrupt(bundle); using EvidenceStore store = new(directory);
            Check(Fails(() => store.Read(original.Manifest.Terminal.Session, original.Manifest.Terminal.Cycle)), label);
        }
        Case("hash", d => { string path = Path.Combine(d, "image.pgm"); byte[] b = File.ReadAllBytes(path); b[^1] ^= 1; File.WriteAllBytes(path, b); });
        Case("truncated", d => { using FileStream f = new(Path.Combine(d, "image.pgm"), FileMode.Open); f.SetLength(5); });
        Case("missing", d => File.Move(Path.Combine(d, "image.pgm"), Path.Combine(d, "lost.pgm")));
        Case("extra", d => File.WriteAllText(Path.Combine(d, "extra.txt"), "unexpected"));
        Case("schema", d => Edit(d, n => n["Schema"] = 2));
        Case("unknown-field", d => Edit(d, n => n["Unexpected"] = 1));
        Case("wrong-cycle", d => Edit(d, n => n["Terminal"]!["Cycle"] = 99));
        Case("wrong-statistics", d => Edit(d, n => n["Inspection"]!["Measurement"]!["Mean"] = 0));
        Case("bad-roi", d => Edit(d, n => n["Recipe"]!["Region"]!["X"] = -1));
        Case("oversized", d => { using FileStream f = new(Path.Combine(d, "manifest.json"), FileMode.Open); f.SetLength(65537); });
        Case("duplicate", d => { string path = Path.Combine(d, "manifest.json"); string text = File.ReadAllText(path); File.WriteAllText(path, text.Replace("\"Schema\": 1", "\"Schema\": 1, \"Schema\": 1")); });
        Case("null-order", d => Edit(d, n => n["Terminal"]!["Order"] = null));
        Case("missing-required", d => Edit(d, n => n.AsObject().Remove("Schema")));
        Case("invalid-enum", d => Edit(d, n => n["Terminal"]!["Judgment"] = "Maybe"));
        Case("wrong-frame-source", d => Edit(d, n => n["Frame"]!["Number"] = 99));
        Case("processing-failure", d => Edit(d, n => n["Inspection"]!["Stage"] = "Failed"));
        Case("empty-reason", d => Edit(d, n => n["Terminal"]!["Reason"] = ""));
        string pending = Path.Combine(root, "pending"); Directory.CreateDirectory(Path.Combine(pending, "abandoned.pending"));
        Check(Fails(() => new EvidenceStore(pending)), "orphan pending blocks reopen");
        static void Edit(string d, Action<JsonNode> edit)
        { string path = Path.Combine(d, "manifest.json"); JsonNode n = JsonNode.Parse(File.ReadAllText(path))!; edit(n); File.WriteAllText(path, n.ToJsonString()); }
    }
    private static async Task WriteFailure()
    {
        IntegratedStationPort p = new(); IntegratedStationSession s = await IntegratedStationSession.OpenAsync(Path.Combine(root, "failure-ledger"), p, new ManualStationClock());
        await Prepare(s, p); await s.StartAsync("failure", "demo-ok-v1"); await Finish(s, p);
        string storage = Path.Combine(root, "blocked"); using EvidenceStore store = new(storage); EvidenceWorkflow flow = new(s, p, store);
        StationTerminal terminal = s.Snapshot.Host.Station.Terminal!;
        string target = terminal.Session.ToString("N") + "-" + terminal.Cycle.ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(Path.Combine(storage, target), "Injected destination file conflict");
        Check(await FailsAsync(() => flow.SaveThenAcknowledgeAsync()), "publication failure surfaced");
        Check(!s.Snapshot.Host.Station.TerminalConsumed && s.Devices.Vision.RetainedResults == 1 && !s.Devices.Vision.CycleAcknowledged, "save failure retains original terminal and vision receipt");
        Check(s.Snapshot.Ledger.Cycles[0].Terminal is null && s.Devices.Vision.FramesAcquired == 1, "failed evidence never stores terminal or recaptures");
        Check(await FailsAsync(() => flow.SaveThenAcknowledgeAsync()), "failed store does not silently retry partial publication");
        using EvidenceStore repaired = new(Path.Combine(root, "reviewed-new-store"));
        EvidenceReceipt receipt = await new EvidenceWorkflow(s, p, repaired).SaveThenAcknowledgeAsync();
        Check(receipt.Manifest.Terminal == terminal && s.Devices.Vision.MeasureCalls == 1, "explicit new destination saves same retained pixels with no reprocessing");
        await s.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer); await Close(s, p);
        store.Dispose(); Check(Fails(() => new EvidenceStore(storage)), "pending evidence persists and blocks reopening failed store");
    }
    private static async Task StopWithoutFrame()
    {
        IntegratedStationPort p = new(); IntegratedStationSession s = await IntegratedStationSession.OpenAsync(Path.Combine(root, "stop-ledger"), p, new ManualStationClock());
        await Prepare(s, p); await s.StartAsync("stop", "demo-ok-v1");
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (s.Devices.Motion.MoveCalls == 0) await Task.Delay(1, timeout.Token);
        p.AdvanceSimulation(.03); await s.StopFlowAsync(); p.RequestOrdinaryStopSimulation();
        while (s.Snapshot.Host.TaskActive || s.Snapshot.Host.Station.Pending is not null) { p.AdvanceSimulation(.01); await Task.Delay(1, timeout.Token); }
        Check(Fails(() => p.ExportEvidence(s.Snapshot)) && s.Devices.Vision.FramesAcquired == 0, "stopped cycle gets no invented successful image evidence");
        await s.SaveResultAsync(); await Close(s, p);
    }
    public static async Task DemoAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "hnova-station-evidence-demo-" + Guid.NewGuid().ToString("N"));
        IntegratedStationPort p = new(); IntegratedStationSession s = await IntegratedStationSession.OpenAsync(Path.Combine(directory, "ledger"), p, new ManualStationClock());
        using EvidenceStore store = new(Path.Combine(directory, "evidence")); await Prepare(s, p);
        foreach (string recipe in new[] { "demo-ok-v1", "demo-ng-v1" })
        {
            await s.StartAsync("demo-part", recipe); await Finish(s, p);
            EvidenceReceipt r = await new EvidenceWorkflow(s, p, store).SaveThenAcknowledgeAsync();
            Console.WriteLine($"{r.Manifest.Terminal.Judgment}: frame {r.Manifest.Frame.Number}; mean {r.Manifest.Inspection.Measurement!.Mean}; {r.DirectoryPath}");
            await s.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer);
        }
        await Close(s, p); Console.WriteLine($"OFFLINE ONLY; original P5 images/manifests retained: {directory}; closed={s.Snapshot.Closed}");
    }
}
