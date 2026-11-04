using System.Security.Cryptography;
using IndustrialStation;
using IndustrialStationIntegrated;
using IndustrialStationMotion;
using IndustrialStationVision;
using IndustrialVision;
namespace IndustrialStationEvidence;

internal static class ReconciliationChecks
{
    private static string root = "";
    private static int count;
    private static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); count++; }
    private static bool Fails(Action f) { try { f(); return false; } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException) { return true; } }
    private static async Task Prepare(IntegratedStationSession s, IntegratedStationPort p)
    {
        s.OpenMotionSimulation(); s.EnableMotionSimulation(); s.OpenReplay(); Task<bool> home = s.BeginReferenceSimulation();
        for (int i = 0; i < 1000 && !home.IsCompleted; i++) p.AdvanceSimulation(.01);
        if (!await home.WaitAsync(TimeSpan.FromSeconds(2))) throw new InvalidOperationException("Home failed");
    }
    private static async Task Wait(IntegratedStationSession s, IntegratedStationPort p, Func<bool> done)
    { using CancellationTokenSource limit = new(TimeSpan.FromSeconds(10)); while (!done()) { p.AdvanceSimulation(.01); await Task.Delay(1, limit.Token); } }
    private static async Task Close(IntegratedStationSession s, IntegratedStationPort p)
    { using CancellationTokenSource limit = new(TimeSpan.FromSeconds(10)); s.RequestClose(); while (!await s.CloseStepAsync()) { p.TryAdvanceSimulation(.01); await Task.Delay(1, limit.Token); } }
    private static Dictionary<string, string> Hashes(string directory) => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(directory, p), p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
    public static async Task RunAsync()
    {
        root = Path.Combine(Path.GetTempPath(), "hnova-reconciliation-checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        await ActualStop(); await ActualLate(StepKind.Capture); await ActualLate(StepKind.Inspect); await ActualProcessingFailure(); await Inventory();
        Console.WriteLine($"PASS {count} abnormal/reconciliation/read-only checks; files retained at {root}");
    }
    private static async Task ActualStop()
    {
        string dir = Path.Combine(root, "stop"); IntegratedStationPort p = new();
        IntegratedStationSession s = await IntegratedStationSession.OpenAsync(Path.Combine(dir, "ledger"), p, new ManualStationClock());
        using EvidenceStore normal = new(Path.Combine(dir, "normal")); using AbnormalEvidenceStore abnormal = new(Path.Combine(dir, "abnormal"));
        EvidenceWorkflow workflow = new(s, p, normal); await Prepare(s, p); await s.StartAsync("stop", "demo-ok-v1");
        await Wait(s, p, () => s.Devices.Motion.MoveCalls == 1); p.AdvanceSimulation(.03); await s.StopFlowAsync();
        Check(Fails(() => AbnormalEvidence.Capture(s.Snapshot, s.Devices)), "active motion/pending cannot export settled diagnostics");
        p.RequestOrdinaryStopSimulation(); await Wait(s, p, () => !s.Snapshot.Host.TaskActive && s.Snapshot.Host.Station.Pending is null);
        AbnormalEvidence record = await workflow.SaveAbnormalThenAcknowledgeAsync(abnormal);
        Check(record.FrameState == AbnormalFrameState.NoQualifiedFrame && record.DiagnosticFrame is null && record.DiagnosticMeasurement is null, "stop without capture has no fabricated image/statistics");
        Check(s.Snapshot.Host.Station.TerminalConsumed && s.Devices.Vision.FramesAcquired == 0, "diagnostic save precedes terminal consumption without capture");
        var stale = s.Devices with { Vision = s.Devices.Vision with { CaptureOperation = new(Guid.NewGuid(), 1, 2, StepKind.Capture), FrameId = Guid.NewGuid(), FrameStamp = new FrameStamp(Guid.NewGuid(), 1), Workpiece = "previous" } };
        Check(AbnormalEvidence.Capture(s.Snapshot, stale).FrameState == AbnormalFrameState.NoQualifiedFrame, "old-cycle preview/frame metadata cannot attach to current abnormal cycle");
        Check(Fails(() => p.ExportAbnormalFrame(s.Snapshot)), "no frame is invented for stopped cycle");
        string file = abnormal.Save(record); Check(abnormal.Save(record) == file, "identical abnormal save idempotent");
        Check(Fails(() => abnormal.Save(record with { Terminal = record.Terminal with { Reason = "conflict" } })), "conflicting abnormal terminal rejected");
        Check(Fails(() => AbnormalEvidence.Validate(record with { FrameState = AbnormalFrameState.AcceptedFrame })), "false frame claim rejected");
        Check(Fails(() => AbnormalEvidence.Validate(record with { Terminal = record.Terminal with { Outcome = CycleOutcome.Completed } })), "completed cycle cannot be abnormal evidence");
        await Close(s, p); normal.Dispose(); abnormal.Dispose();
        ReconciliationReport report = EvidenceReconciler.Inspect(Path.Combine(dir, "ledger"), Path.Combine(dir, "normal"), Path.Combine(dir, "abnormal"));
        Check(report.Rows.Single().Match == EvidenceMatch.MatchedAbnormal && report.RequiresAttention && !report.Rows[0].ReviewRecorded, "matched files do not imply reviewed/released");
    }
    private static async Task ActualLate(StepKind step)
    {
        string dir = Path.Combine(root, "late-" + step);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously), release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IntegratedStationPort p = new(k => { if (k != step) return Task.CompletedTask; entered.TrySetResult(); return release.Task; });
        ManualStationClock clock = new(); IntegratedStationSession s = await IntegratedStationSession.OpenAsync(Path.Combine(dir, "ledger"), p, clock);
        using EvidenceStore normal = new(Path.Combine(dir, "normal")); using AbnormalEvidenceStore abnormal = new(Path.Combine(dir, "abnormal"));
        await Prepare(s, p); await s.StartAsync("late", "demo-ok-v1"); await Wait(s, p, () => entered.Task.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(step == StepKind.Capture ? 1 : 3)); await Wait(s, p, () => s.Snapshot.Host.Station.Stage == StationStage.Faulted);
        StationTerminal terminal = s.Snapshot.Host.Station.Terminal!;
        Check(Fails(() => AbnormalEvidence.Capture(s.Snapshot, s.Devices)), "deadline alone not settled diagnostic evidence");
        release.SetResult(); await Wait(s, p, () => !s.Snapshot.Host.TaskActive && s.Snapshot.Host.Station.Pending is null);
        AbnormalEvidence originalDiagnostic = AbnormalEvidence.Capture(s.Snapshot, s.Devices);
        OwnedFrame originalFrame = p.ExportAbnormalFrame(s.Snapshot);
        string archive = AbnormalImageArchive.Save(Path.Combine(dir, "images"), originalDiagnostic, originalFrame);
        Check(AbnormalImageArchive.Save(Path.Combine(dir, "images"), originalDiagnostic, originalFrame) == archive, "original image archive retry idempotent");
        var restored = AbnormalImageArchive.Read(archive);
        Check(AbnormalImageArchive.Restore(restored).CopyPixels().SequenceEqual(originalFrame.Image.CopyPixels()), "late original pixels survive archive roundtrip");
        Check(restored.Diagnostic.Terminal == terminal && restored.Scope == "AbnormalPixels-DiagnosticOnly", "archive preserves original fault and diagnostic scope");
        AbnormalEvidence record = await new EvidenceWorkflow(s, p, normal).SaveAbnormalThenAcknowledgeAsync(abnormal);
        Check(ReferenceEquals(terminal, record.Terminal) && terminal.Outcome == CycleOutcome.Faulted && terminal.Judgment is null, "late diagnostic data never rewrites terminal");
        Check(record.FrameState == (step == StepKind.Capture ? AbnormalFrameState.UnacceptedFrame : AbnormalFrameState.AcceptedFrame), "accepted/unaccepted frame distinction");
        Check(record.DiagnosticFrame is not null && record.DiagnosticJudgment == (step == StepKind.Inspect ? Verdict.Ok : null), "late result kept diagnostic only");
        Check(Fails(() => AbnormalEvidence.Validate(record with { CaptureOperation = record.CaptureOperation! with { Cycle = record.Terminal.Cycle + 1 } })), "wrong-cycle capture operation rejected");
        if (step == StepKind.Inspect)
            Check(Fails(() => AbnormalEvidence.Validate(record with { DiagnosticMeasurement = record.DiagnosticMeasurement! with { Mean = double.NaN } })), "nonfinite diagnostic statistic rejected");
        Check(s.Devices.Vision.PublishCalls == 0 && s.Devices.Vision.MeasureCalls == (step == StepKind.Inspect ? 1 : 0), "late evidence does not publish or repeat inspect");
        Check(s.Devices.Vision.RetainedResults == 0 && s.Devices.Vision.CycleAcknowledged, "saved abnormal terminal permits original receipt acknowledgment");
        Check(Fails(() => p.ExportAbnormalFrame(s.Snapshot)), "acknowledged abnormal frame cannot be re-exported");
        await Close(s, p);
    }
    private static async Task ActualProcessingFailure()
    {
        string dir = Path.Combine(root, "processing-failure");
        VisionStationPort vision = new(PlainPgm.LoadFixture(), (_, _) => throw new InvalidOperationException("Injected algorithm failure"));
        StationApplication app = await StationApplication.OpenWithPortAsync(Path.Combine(dir, "ledger"), vision, new ManualStationClock()); vision.OpenReplay();
        await app.StartAsync("bad-processing", "demo-ok-v1"); using CancellationTokenSource limit = new(TimeSpan.FromSeconds(10));
        while (app.Snapshot.Host.Station.Terminal is null || app.Snapshot.Host.TaskActive) await Task.Delay(1, limit.Token);
        await using MotionStationPort dummyMotion = new();
        IntegratedPortSnapshot pair = new(dummyMotion.Snapshot, vision.Snapshot, null, false, false, false);
        AbnormalEvidence record = AbnormalEvidence.Capture(app.Snapshot, pair);
        Check(record.FrameState == AbnormalFrameState.AcceptedFrame && record.ProcessingError is not null && record.DiagnosticJudgment is null && record.DiagnosticMeasurement is null,
            "actual algorithm exception remains failure, never NG");
        string imageArchive = AbnormalImageArchive.Save(Path.Combine(dir, "images"), record, vision.ExportAbnormalFrame(app.Snapshot));
        Check(AbnormalImageArchive.Read(imageArchive).Diagnostic.ProcessingError == record.ProcessingError, "actual failed inspection keeps original pixels without invented measurement");
        using AbnormalEvidenceStore store = new(Path.Combine(dir, "abnormal")); store.Save(record); await app.SaveResultAsync(); await vision.AcknowledgeAfterSavedAsync(app.Snapshot);
        var checkedImage = AbnormalImageArchive.Read(imageArchive);
        Check(Fails(() => AbnormalImageArchive.Restore(checkedImage with { Schema = 99 })), "unknown archive schema refused");
        checkedImage.Pixels[0] ^= 1;
        Check(Fails(() => AbnormalImageArchive.Restore(checkedImage)), "changed pixels fail hash before restoration");
        string brokenArchive = imageArchive + ".broken"; File.WriteAllText(brokenArchive, "{");
        Check(Fails(() => AbnormalImageArchive.Read(brokenArchive)), "broken archive cannot become valid image evidence");
        Check(app.Snapshot.Host.Station.Judgment is null && vision.Snapshot.MeasureCalls == 1, "failure persistence performs no retry or business verdict");
        Check(Fails(() => AbnormalEvidence.Validate(record with { DiagnosticJudgment = Verdict.Ng })), "processing failure cannot acquire invented NG diagnostic judgment");
        app.RequestClose(); while (!await app.CloseStepAsync()) await Task.Delay(1, limit.Token);
    }
    private static async Task Inventory()
    {
        string dir = Path.Combine(root, "inventory"), journal = Path.Combine(dir, "ledger"), normalDir = Path.Combine(dir, "normal"), abnormalDir = Path.Combine(dir, "abnormal");
        IntegratedStationPort p = new(); IntegratedStationSession s = await IntegratedStationSession.OpenAsync(journal, p, new ManualStationClock());
        using EvidenceStore normal = new(normalDir); using AbnormalEvidenceStore abnormal = new(abnormalDir);
        await Prepare(s, p); await s.StartAsync("normal", "demo-ok-v1"); await Wait(s, p, () => s.Snapshot.Host.Station.Stage == StationStage.Completed && !s.Snapshot.Host.TaskActive);
        VisionEvidence evidence = p.ExportEvidence(s.Snapshot); EvidenceReceipt receipt = normal.Save(evidence);
        Check(EvidenceReconciler.Inspect(journal, normalDir, abnormalDir).StorageIssues.Any(i => i.StartsWith("LedgerUnavailable")), "live ledger writer blocks read-only inventory");
        await Close(s, p); normal.Dispose(); abnormal.Dispose();
        string source = receipt.DirectoryPath;
        ReconciliationReport Read() => EvidenceReconciler.Inspect(journal, normalDir, abnormalDir);
        var before = Hashes(dir); ReconciliationReport matched = Read(); var after = Hashes(dir);
        Check(matched.Rows.Single().Match == EvidenceMatch.MatchedNormal && matched.RequiresAttention, "normal verified but review still required");
        Check(before.Count == after.Count && before.All(k => after.GetValueOrDefault(k.Key) == k.Value), "inventory changes no file names or content");
        using (StationLedger read = StationLedger.OpenReadOnly(journal))
        {
            Check(Fails(() => read.SaveTerminal(evidence.Terminal)), "read-only ledger rejects SaveTerminal");
            Check(Fails(() => read.Begin(Guid.NewGuid(), 1, evidence.Terminal.Order)), "read-only ledger rejects Begin");
            Check(Fails(() => read.Review(evidence.Terminal.Session, 1, Review())), "read-only ledger rejects Review");
        }
        using (EvidenceStore busy = new(normalDir)) Check(Read().StorageIssues.Count > 0, "active evidence writer reported unavailable, not healthy");
        File.WriteAllText(Path.Combine(normalDir, "unrecognized.txt"), "preserved");
        Directory.CreateDirectory(Path.Combine(normalDir, "unfinished.pending"));
        File.WriteAllText(Path.Combine(abnormalDir, "interrupted.json.pending"), "partial");
        ReconciliationReport pending = Read();
        Check(pending.StorageIssues.Any(i => i.StartsWith("UnexpectedEntry")), "unknown entry flagged");
        Check(pending.StorageIssues.Count(i => i.StartsWith("Pending:")) == 2 && pending.Rows[0].Match == EvidenceMatch.MatchedNormal, "pending reports coexist with independent valid bundle read");
        Guid orphan = Guid.NewGuid(); CopyBundle(source, normalDir, orphan, 1, evidence.Terminal with { Session = orphan });
        Check(Read().Rows.Any(r => r.Session == orphan && r.Match == EvidenceMatch.EvidenceWithoutIntent), "orphan evidence never manufactures intent");
        Guid unknown = Guid.NewGuid(); WorkOrder work = evidence.Terminal.Order with { Number = 2 };
        using (StationLedger l = new(journal))
        {
            l.Review(evidence.Terminal.Session, 1, Review()); l.Begin(unknown, 1, work);
        }
        Check(Read().Rows.Single(r => r.Session == unknown).Match == EvidenceMatch.IntentWithoutTerminal, "intent-only remains unknown");
        CopyBundle(source, normalDir, unknown, 1, evidence.Terminal with { Session = unknown, Order = work });
        Check(Read().Rows.Single(r => r.Session == unknown).Match == EvidenceMatch.EvidenceWithoutTerminal, "evidence cannot fill unknown terminal");
        using (StationLedger l = new(journal)) l.SaveTerminal(evidence.Terminal with { Session = unknown, Order = work, Reason = "different" });
        Check(Read().Rows.Single(r => r.Session == unknown).Match == EvidenceMatch.TerminalConflict, "different terminal reported conflict");
        string image = Path.Combine(source, "image.pgm"); using (FileStream f = new(image, FileMode.Open, FileAccess.Write)) f.SetLength(4);
        Check(Read().Rows.Single(r => r.Session == evidence.Terminal.Session).Match == EvidenceMatch.InvalidEvidence, "corrupt image distinct from missing evidence");
        string missingDir = Path.Combine(root, "missing"); ReconciliationReport missing = EvidenceReconciler.Inspect(missingDir, normalDir, abnormalDir);
        Check(missing.StorageIssues.Count == 1 && missing.RequiresAttention && !Directory.Exists(missingDir), "read-only missing ledger never creates directory/lock");
        // A separate root pair establishes missing/duplicate abnormal classifications without altering prior evidence.
        string emptyNormal = Path.Combine(dir, "empty-normal"); using (EvidenceStore empty = new(emptyNormal)) { }
        Check(EvidenceReconciler.Inspect(journal, emptyNormal, abnormalDir).Rows.Any(r => r.Match == EvidenceMatch.MissingEvidence), "terminal without evidence reported missing");
        AbnormalEvidence stopped = new(1, "DiagnosticOnly-NoPixels", evidence.Terminal with { Outcome = CycleOutcome.Stopped, FrameId = null, Judgment = null }, AbnormalFrameState.NoQualifiedFrame, null, null, null, null, null, null);
        Check(Fails(() => new AbnormalEvidenceStore(abnormalDir)), "pending blocks abnormal writer reopening");
        string cleanAbnormal = Path.Combine(dir, "clean-abnormal");
        using (AbnormalEvidenceStore duplicate = new(cleanAbnormal))
            duplicate.Save(stopped with { Terminal = stopped.Terminal with { Session = orphan } });
        Check(EvidenceReconciler.Inspect(journal, normalDir, cleanAbnormal).Rows.Single(r => r.Session == orphan).Match == EvidenceMatch.DuplicateEvidence,
            "normal and diagnostic evidence for same cycle require conflict review");
        Guid bad = Guid.NewGuid(); string badFile;
        using (AbnormalEvidenceStore writer = new(cleanAbnormal)) badFile = writer.Save(stopped with { Terminal = stopped.Terminal with { Session = bad } });
        string malformed = File.ReadAllText(badFile); File.WriteAllText(badFile, malformed.Replace("\"Schema\": 1", "\"Schema\": 1, \"Schema\": 1"));
        Check(EvidenceReconciler.Inspect(journal, normalDir, cleanAbnormal).Rows.Single(r => r.Session == bad).Match == EvidenceMatch.InvalidEvidence,
            "duplicate JSON fields rejected in abnormal file");
        Check(Fails(() => AbnormalEvidence.Validate(stopped with { FrameState = (AbnormalFrameState)99 })), "unknown abnormal classification rejected");
        Check(Fails(() => AbnormalEvidence.Validate(stopped with { DiagnosticJudgment = Verdict.Ok })), "diagnostic judgment cannot appear without measurement/frame");
        Check(Fails(() => AbnormalEvidence.Validate(stopped with { Kind = "ImageSaved" })), "diagnostic record cannot claim image bytes");
        string failedRoot = Path.Combine(dir, "failed-abnormal");
        using (AbnormalEvidenceStore failed = new(failedRoot))
        {
            Directory.CreateDirectory(Path.Combine(failedRoot, AbnormalEvidenceStore.Name(stopped.Terminal.Session, stopped.Terminal.Cycle)));
            Check(Fails(() => failed.Save(stopped)), "abnormal publication failure surfaced");
            Check(Fails(() => failed.Save(stopped)), "failed abnormal store refuses automatic retry");
        }
        Check(Directory.EnumerateFiles(failedRoot, "*.pending").Count() == 1, "failed abnormal publication preserves partial file");
    }
    private static RestartReview Review() => new("offline-test", "fixture-only", DateTimeOffset.UtcNow, true, true, true, true, PeerResultDisposition.ConfirmedNotPublished);
    private static void CopyBundle(string source, string root, Guid session, long cycle, StationTerminal terminal)
    {
        string destination = Path.Combine(root, session.ToString("N") + "-" + cycle.ToString("D10", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(destination); File.Copy(Path.Combine(source, "image.pgm"), Path.Combine(destination, "image.pgm"));
        string text = File.ReadAllText(Path.Combine(source, "manifest.json")); var node = System.Text.Json.Nodes.JsonNode.Parse(text)!;
        node["Terminal"] = System.Text.Json.JsonSerializer.SerializeToNode(terminal, new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
        File.WriteAllText(Path.Combine(destination, "manifest.json"), node.ToJsonString());
    }
    public static async Task ImageDemoAsync()
    {
        root = Path.Combine(Path.GetTempPath(), "hnova-image-demo-" + Guid.NewGuid().ToString("N"));
        await ActualLate(StepKind.Inspect);

        string directory = Path.Combine(root, "late-Inspect");
        string ledger = Path.Combine(directory, "ledger");
        string normal = Path.Combine(directory, "normal");
        string abnormal = Path.Combine(directory, "abnormal");
        string images = Path.Combine(directory, "images");
        ImageEvidenceReport report = ImageEvidenceReconciler.Inspect(ledger, normal, abnormal, images);
        ImageEvidenceRow row = report.Images.Single();
        Check(row.State == ImageEvidenceState.Verified && report.RequiresAttention &&
            !report.BaseReport.Rows.Single().ReviewRecorded, "demo keeps the verified original image and outstanding review");

        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            Ledger = ledger,
            Normal = normal,
            Abnormal = abnormal,
            Images = images,
            row.Session,
            row.Cycle
        }));
    }

    public static async Task DemoAsync()
    {
        root = Path.Combine(Path.GetTempPath(), "hnova-reconciliation-demo-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        await ActualStop(); string d = Path.Combine(root, "stop");
        ReconciliationReport r = EvidenceReconciler.Inspect(Path.Combine(d, "ledger"), Path.Combine(d, "normal"), Path.Combine(d, "abnormal"));
        foreach (ReconciliationRow row in r.Rows) Console.WriteLine($"cycle={row.Cycle}; match={row.Match}; reviewed={row.ReviewRecorded}; {row.Detail}");
        Console.WriteLine($"OFFLINE; attention={r.RequiresAttention}; no automatic review or release; files={root}");
    }
}
