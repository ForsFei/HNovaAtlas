namespace IndustrialStation;

internal static class ApplicationChecks
{
    private static int count;
    private static string root = "";
    public static async Task RunAsync()
    {
        root = Path.Combine(Path.GetTempPath(), "hnova-station-app-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (StationRecipe recipe in StationApplication.Recipes) { recipe.Validate(); Check(recipe.SimulatedDelayMilliseconds <= 250, "fixed bounded recipe"); }
        Check(Fails(() => new StationRecipe("bad", (Verdict)99, 1).Validate()), "invalid verdict recipe");
        Check(Fails(() => new RecipeSimulationPort([new("duplicate", Verdict.Ok, 0), new("duplicate", Verdict.Ng, 0)])), "duplicate recipe version rejected");
        await NormalAsync(); await RestartAsync(); await CloseDuringRunAsync(); await AdmissionSlotAsync(); await FailedIntentAsync();
        Console.WriteLine($"PASS {count} application/recipe/durable-order/close checks; files retained at {root}");
    }
    private static string NewDirectory() => Path.Combine(root, Guid.NewGuid().ToString("N"));
    private static async Task NormalAsync()
    {
        string directory = NewDirectory(); TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool intentSeen = false;
        ScriptedStationPort port = new(async command =>
        {
            if (command.Key.Step == StepKind.Move)
            {
                intentSeen = Directory.GetFiles(directory, "*.json").Length == 1;
                await gate.Task;
            }
            return OfflineSimulation.Reply(command, command.Order.RecipeVersion == "demo-ng-v1" ? Verdict.Ng : Verdict.Ok);
        });
        StationApplication app = await StationApplication.OpenForChecksAsync(() => new(directory), port, new ManualStationClock());
        Check(app.Snapshot.Ledger.CanStartNewCycle && app.Snapshot.Host.Station.Stage == StationStage.Idle, "new app idle");
        Check(await FailsAsync(() => app.StartAsync(" bad ", "demo-ok-v1")), "invalid workpiece rejected");
        Check(await FailsAsync(() => app.StartAsync("part", "other")) && port.ExecuteCalls == 0, "unknown recipe never dispatches");
        Check(await app.StartAsync("part-ng", "demo-ng-v1") == Admission.Accepted, "app accepted captured recipe");
        await Until(app, s => s.Host.TaskActive);
        Check(!app.Snapshot.Ledger.CanStartNewCycle && app.Snapshot.Host.Station.Order!.RecipeVersion == "demo-ng-v1", "active immutable recipe and durable block");
        Check(await FailsAsync(() => app.StartAsync("part-2", "demo-ok-v1")), "active cycle rejects recipe switch/new workpiece");
        Check(await FailsAsync(() => app.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer)), "cannot review running task");
        gate.TrySetResult(); await Until(app, s => s.Host.Station.Stage == StationStage.Completed);
        Check(intentSeen && port.ExecuteCalls == 4 && port.Maximum == 1, "intent precedes actual simulated dispatch, four serial steps");
        Check(app.Snapshot.Host.Station.Terminal!.Judgment == Verdict.Ng, "captured NG recipe stays NG");
        Check(await FailsAsync(() => app.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer)), "must save before review");
        Check(await app.SaveResultAsync(), "save then consume");
        Check(app.Snapshot.Host.Station.TerminalConsumed && app.Snapshot.Ledger.Cycles[0].Terminal == app.Snapshot.Host.Station.Terminal, "persisted terminal matches consumed original");
        Check(await app.SaveResultAsync() && Directory.GetFiles(directory, "*.json").Length == 2, "repeat save no duplicate file");
        Check(await FailsAsync(() => app.SimulateReviewAndReleaseAsync((PeerResultDisposition)99)), "explicit known peer disposition required");
        Check(await app.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer), "explicit simulation review releases");
        Check(app.Snapshot.Host.Station.Stage == StationStage.Idle && app.Snapshot.Ledger.CanStartNewCycle, "both ledger and host permit next");
        await app.StartAsync("part-ok", "demo-ok-v1"); await Until(app, s => s.Host.Station.Stage == StationStage.Completed);
        Check(app.Snapshot.Host.Station.Order!.Number == 2 && app.Snapshot.Host.Station.Judgment == Verdict.Ok, "next recipe captured and number increased");
        await app.SaveResultAsync(); await app.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer);
        await Close(app);
        Check(app.Snapshot.Closed && app.Snapshot.Host.PortDisposed && port.DisposeCalls == 1, "close joins host then releases resources");
        Check(await FailsAsync(() => app.StartAsync("x", "demo-ok-v1")), "closed app rejects actions");
        using StationLedger reopened = new(directory);
        Check(reopened.Report.CanStartNewCycle && reopened.Report.Cycles.Count == 2, "two reviewed cycles survived reopen");
    }
    private static async Task RestartAsync()
    {
        string dir = NewDirectory(); Guid old = Guid.NewGuid();
        using (StationLedger ledger = new(dir)) ledger.Begin(old, 1, new(StationApplication.DemoProducer, 9, "interrupted", "demo-ng-v1"));
        ScriptedStationPort port = new(c => Task.FromResult<StationReply?>(OfflineSimulation.Reply(c)));
        StationApplication app = await StationApplication.OpenForChecksAsync(() => new(dir), port, new ManualStationClock());
        Check(app.Snapshot.Host.Station.Session != old && !app.Snapshot.Ledger.CanStartNewCycle, "new host session does not restore interrupted cycle");
        Check(await FailsAsync(() => app.StartAsync("new", "demo-ok-v1")) && port.ExecuteCalls == 0, "restart gate no replay");
        await app.SimulateReviewAndReleaseAsync(PeerResultDisposition.ConfirmedNotPublished);
        Check(app.Snapshot.Ledger.Cycles[0].Terminal is null && app.Snapshot.Ledger.Cycles[0].Review is not null, "unknown old terminal not invented");
        await app.StartAsync("new", "demo-ok-v1"); await Until(app, s => s.Host.Station.Stage == StationStage.Completed);
        Check(app.Snapshot.Host.Station.Order!.Number == 10, "persistent source number resumed from high water");
        await Close(app);
        using StationLedger reopened = new(dir);
        Check(reopened.Report.Cycles.Last().Terminal is not null && !reopened.Report.CanStartNewCycle, "close saved terminal without auto review");
    }
    private static async Task CloseDuringRunAsync()
    {
        string dir = NewDirectory(); TaskCompletionSource<StationReply?> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<StationCommand> taken = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedStationPort port = new(c => { taken.TrySetResult(c); return gate.Task; });
        StationApplication app = await StationApplication.OpenForChecksAsync(() => new(dir), port, new ManualStationClock());
        await app.StartAsync("closing", "demo-ng-v1"); await Until(app, s => s.Host.TaskActive);
        StationCommand command = await taken.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await app.StopAsync(); await Until(app, s => s.Host.Station.Stage == StationStage.Stopped);
        Check(app.Snapshot.Host.TaskActive && app.Snapshot.Host.Station.Pending is not null, "software stop preserves actual task and obligation");
        app.RequestClose(); await Until(app, s => s.Host.Station.Stage == StationStage.Closing);
        Check(!await app.CloseStepAsync(), "close step finite while actual task active");
        Check(app.Snapshot.Host.Station.TerminalConsumed && app.Snapshot.Ledger.Cycles[0].Terminal!.Outcome == CycleOutcome.Stopped, "close saves stopped terminal before consuming");
        Check(port.DisposeCalls == 0 && !app.Snapshot.Closed, "close does not dispose active port");
        Check(await FailsAsync(() => app.StartAsync("no", "demo-ok-v1")), "close prevents new intent");
        gate.TrySetResult(OfflineSimulation.Reply(command)); await Close(app);
        Check(port.ExecuteCalls == 1 && port.DisposeCalls == 1, "late completion drains, no next stage");
        Check(!app.Snapshot.Ledger.CanStartNewCycle, "close never auto reviews cycle");
    }
    private static async Task AdmissionSlotAsync()
    {
        string dir = NewDirectory(); using ManualResetEventSlim writing = new(false), release = new(false);
        ScriptedStationPort port = new(c => Task.FromResult<StationReply?>(OfflineSimulation.Reply(c)));
        StationApplication app = await StationApplication.OpenForChecksAsync(() => new(dir, p =>
        { if (p == CommitPoint.BeforePublish) { writing.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); } }), port);
        Task<Admission> starting = app.StartAsync("writing", "demo-ok-v1");
        if (!await Task.Run(() => writing.Wait(TimeSpan.FromSeconds(10)))) throw new TimeoutException();
        Check(app.Snapshot.Busy, "disk write uses background application slot");
        Check(await FailsAsync(() => app.SaveResultAsync()), "busy rejects immediately without hidden queue");
        app.RequestClose();
        Check(app.Snapshot.CloseRequested, "close signal independent of busy disk slot");
        release.Set(); Check(await FailsAsync(() => starting), "close during intent write blocks subsequent Start");
        await Close(app);
        Check(port.ExecuteCalls == 0 && app.Snapshot.Ledger.Cycles.Count == 1 && app.Snapshot.Ledger.Cycles[0].Terminal is null, "saved unused intent preserved, no dispatch");
    }
    private static async Task FailedIntentAsync()
    {
        string dir = NewDirectory(); ScriptedStationPort port = new(c => Task.FromResult<StationReply?>(OfflineSimulation.Reply(c)));
        StationApplication app = await StationApplication.OpenForChecksAsync(() => new(dir, _ => throw new IOException("injected save failure")), port);
        Check(await FailsAsync(() => app.StartAsync("no-send", "demo-ok-v1")), "failed intent surfaced");
        Check(port.ExecuteCalls == 0 && !app.Snapshot.Ledger.CanStartNewCycle && app.Snapshot.Error is not null, "failed intent no dispatch and blocked error");
        await Close(app); // No physical command or local terminal exists; safe to release lock, retain .tmp for inspection.
        Check(Fails(() => { using StationLedger bad = new(dir); }), "failed temp survives close and blocks reopen");
    }
    internal static async Task Until(StationApplication app, Func<ApplicationSnapshot, bool> condition)
    {
        using CancellationTokenSource stop = new(TimeSpan.FromSeconds(10));
        while (true)
        {
            stop.Token.ThrowIfCancellationRequested(); ApplicationSnapshot current = app.Snapshot; if (condition(current)) return;
            await app.ObserveHostAsync(current.Host.Revision).WaitAsync(stop.Token);
        }
    }
    internal static async Task Close(StationApplication app)
    {
        using CancellationTokenSource stop = new(TimeSpan.FromSeconds(10)); app.RequestClose();
        while (!await app.CloseStepAsync())
        {
            stop.Token.ThrowIfCancellationRequested();
            await Task.Delay(1, stop.Token); // Test cleanup bound only, no production forced-close deadline.
        }
    }
    private static bool Fails(Action action) { try { action(); return false; } catch (Exception e) when (e is ArgumentException or InvalidOperationException or InvalidDataException or IOException) { return true; } }
    private static async Task<bool> FailsAsync(Func<Task> action) { try { await action(); return false; } catch (Exception e) when (e is ArgumentException or InvalidOperationException or InvalidDataException or IOException) { return true; } }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Application check failed: " + message); count++; }
    public static async Task DemoAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "hnova-station-app-" + Guid.NewGuid().ToString("N"));
        StationApplication app = await StationApplication.OpenAsync(directory);
        Console.WriteLine("OFFLINE APPLICATION ONLY; files retained at " + directory);
        await app.StartAsync("demo-workpiece", "demo-ng-v1"); await Until(app, s => s.Host.Station.Stage == StationStage.Completed);
        Console.WriteLine($"Captured={app.Snapshot.Host.Station.Order!.RecipeVersion}; result={app.Snapshot.Host.Station.Judgment}");
        await app.SaveResultAsync();
        Console.WriteLine($"Saved before consumption={app.Snapshot.Ledger.Cycles[0].Terminal is not null}; consumed={app.Snapshot.Host.Station.TerminalConsumed}");
        await app.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer);
        await Close(app); Console.WriteLine($"Closed={app.Snapshot.Closed}; disposed={app.Snapshot.Host.PortDisposed}");
    }
}
