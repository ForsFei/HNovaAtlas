using System.Net;
using System.Net.Sockets;
using IndustrialHandshake;
using IndustrialStation;

namespace IndustrialStationCommunication;

internal static class CommunicationChecks
{
    private static int count;
    private static string root = "";
    private static void Check(bool value, string label) { if (!value) throw new InvalidOperationException("Communication check failed: " + label); count++; }
    public static async Task RunAsync()
    {
        root = Path.Combine(Path.GetTempPath(), "hnova-station-mc-checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        await NormalAsync(); await LostSubmitAsync(); await LostReleaseAsync(); await BusinessFailureAsync(false); await BusinessFailureAsync(true);
        await IdentityAndEpochAsync(); await NotRetainedAsync(); await DeadlineAndCloseAsync();
        Console.WriteLine($"PASS {count} station/MC actual-loopback/identity/reconciliation/release checks; files retained at {root}");
    }
    private static string DirectoryName() => Path.Combine(root, Guid.NewGuid().ToString("N"));
    private static async Task NormalAsync()
    {
        LoopbackController controller = new(); IPEndPoint endpoint = controller.Endpoint;
        CommunicationStationPort port = new(controller);
        CommunicationStationSession session = await CommunicationStationSession.OpenCoreAsync(DirectoryName(), port, new ManualStationClock());
        Check(IPAddress.IsLoopback(endpoint.Address) && endpoint.Port > 0, "only loopback random endpoint");
        Check(await session.StartAsync("part-ng", "demo-ng-v1") == Admission.Accepted, "application accepted after intent persisted");
        await Until(session, s => s.Host.Station.Stage == StationStage.Completed);
        Check(session.Communication.SubmitWrites == 1 && controller.Model.Read().ExecutionCount == 1, "one actual MC submit and one simulated remote execution");
        Check(session.Communication.RemoteTerminalObserved && session.Snapshot.Host.Station.Judgment == Verdict.Ng, "MC move done then simulated NG inspection");
        Check(controller.Model.Read().State == ActionState.Done && session.Communication.ReleaseWrites == 0, "remote result retained after local cycle complete");
        Check(await Fails(() => session.ReleaseRemoteAfterSavedAsync()), "remote release forbidden before saved local result");
        await session.SaveResultAsync();
        Check(await Fails(() => session.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer)), "local release waits remote slot release");
        Check(await Fails(() => session.StartAsync("early", "demo-ok-v1")) && session.Communication.SubmitWrites == 1, "facade blocks new work while remote retained");
        Check(await session.ReleaseRemoteAfterSavedAsync(), "explicit remote release after save");
        Check(await session.ReleaseRemoteAfterSavedAsync() && session.Communication.ReleaseWrites == 1, "known release idempotent no duplicate write2");
        Check(session.Communication.ReadyForNextCycle && controller.Model.Read().State == ActionState.Idle, "released remote slot ready");
        await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer);
        await session.StartAsync("part-ok", "demo-ok-v1"); await Until(session, s => s.Host.Station.Stage == StationStage.Completed);
        Check(session.Communication.RequestId == 2 && session.Communication.SubmitWrites == 2 && controller.Model.Read().ExecutionCount == 2, "next cycle new mapped ticket, no reuse");
        await session.SaveResultAsync(); await session.ReleaseRemoteAfterSavedAsync(); await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer);
        string directory = session.Application.DirectoryPath; await Close(session);
        Check(port.Snapshot.Disposed && session.Snapshot.Host.PortDisposed, "host owns adapter cleanup");
        using StationLedger reopened = new(directory);
        Check(reopened.Report.Cycles.Count == 2 && reopened.Report.CanStartNewCycle, "two saved and reviewed cycles survive close");
        using TcpClient probe = new();
        try { await probe.ConnectAsync(endpoint.Address, endpoint.Port); throw new InvalidOperationException("Listener leaked"); }
        catch (SocketException) { Check(true, "closed adapter stops scan and TCP listener"); }
    }
    private static async Task LostSubmitAsync()
    {
        LoopbackController controller = new() { DropNextSubmitReply = true }; controller.Model.HoldProgress = true;
        CommunicationStationPort port = new(controller);
        CommunicationStationSession session = await CommunicationStationSession.OpenCoreAsync(DirectoryName(), port, new ManualStationClock());
        await session.StartAsync("lost-submit", "demo-ng-v1"); await Until(session, s => s.Host.NeedsReconciliation);
        Check(session.Snapshot.Host.Station.Stage == StationStage.Faulted && session.Communication.SubmitWrites == 1, "lost commit response becomes uncertain station fault");
        Check(session.Communication.BootId == 1 && session.Communication.RequestId == 1 && controller.Model.Read().Control == 1, "mapping and held submit retained");
        controller.Model.HoldProgress = false; await UntilRemote(controller, s => s.State == ActionState.Done); controller.Model.HoldProgress = true;
        ushort revision = controller.Model.Read().Revision; int submits = controller.SubmitWrites;
        Check(await session.ReconcileAsync(), "explicit host query launched"); await Until(session, s => !s.Host.TaskActive && s.Host.Station.Pending is null);
        Check(controller.SubmitWrites == submits && controller.ReleaseWrites == 0 && controller.Model.Read().Control == 1 && controller.Model.Read().Revision == revision,
            "reconnect query performs no submit, release or reset writes");
        Check(controller.Model.Read().ExecutionCount == 1 && session.Snapshot.Host.Station.Terminal!.Outcome == CycleOutcome.Faulted,
            "late known completion drains, never changes original local fault");
        await session.SaveResultAsync();
        Check(await Fails(() => session.ReleaseRemoteAfterSavedAsync()) && !session.Communication.ReleaseAttempted, "held control requires explicit reset before release attempt");
        Check(await session.ResetObservedControlAsync() && controller.Model.Read().Control == 0, "explicit write0 only after observed matching terminal");
        Check(await session.ReleaseRemoteAfterSavedAsync() && controller.SubmitWrites == 1 && controller.ReleaseWrites == 1, "release without command resubmission");
        await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer);
        Check(session.Snapshot.Host.Station.Stage == StationStage.Idle, "local reset only after saved and remote reconciled");
        await Close(session);
    }
    private static async Task LostReleaseAsync()
    {
        LoopbackController controller = new(); CommunicationStationPort port = new(controller);
        CommunicationStationSession session = await CommunicationStationSession.OpenCoreAsync(DirectoryName(), port, new ManualStationClock());
        await session.StartAsync("lost-release", "demo-ok-v1"); await Until(session, s => s.Host.Station.Stage == StationStage.Completed); await session.SaveResultAsync();
        controller.DropNextReleaseReply = true;
        Check(!await session.ReleaseRemoteAfterSavedAsync() && session.Communication.ReleaseAttempted, "lost release response remains uncertain");
        Check(controller.Model.Read().State == ActionState.Idle && controller.Model.Read().Control == 2 && !session.Communication.ReadyForNextCycle,
            "absence alone is not a proved released channel");
        Check(await Fails(() => session.ReleaseRemoteAfterSavedAsync()) && controller.ReleaseWrites == 1, "unknown release never repeats write2");
        Check(await session.ObserveReleaseAsync() && session.Communication.ReleaseObserved && !session.Communication.ReadyForNextCycle,
            "read-only matching release evidence found, held control still blocks");
        Check(controller.ReleaseWrites == 1 && controller.SubmitWrites == 1 && controller.Model.Read().Control == 2, "release observation writes nothing");
        Check(await session.ResetObservedControlAsync() && session.Communication.ReadyForNextCycle, "explicit reset after release evidence permits next cycle");
        await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer); await Close(session);
    }
    private static async Task BusinessFailureAsync(bool executionFault)
    {
        LoopbackController controller = new(); controller.Model.Interlock = executionFault; controller.Model.FailDuringRun = executionFault;
        CommunicationStationPort port = new(controller);
        CommunicationStationSession session = await CommunicationStationSession.OpenCoreAsync(DirectoryName(), port, new ManualStationClock());
        await session.StartAsync("business-fault", "demo-ok-v1"); await Until(session, s => s.Host.Station.Terminal is not null && !s.Host.TaskActive);
        Check(session.Snapshot.Host.Station.Terminal!.Reason == "StepFailed:Move" && session.Snapshot.Host.Station.Pending is null,
            "known business failure maps settled Success=false, not uncertain");
        Check(controller.Model.Read().State == (executionFault ? ActionState.Faulted : ActionState.Rejected) &&
            controller.Model.Read().ExecutionCount == (executionFault ? 1 : 0), "remote rejection and execution fault distinguished");
        Check(session.Snapshot.Host.Station.FrameId is null && session.Snapshot.Host.Station.Judgment is null, "no simulated capture or fabricated judgment after failed Move");
        await session.SaveResultAsync(); Check(await session.ReleaseRemoteAfterSavedAsync(), "failed remote terminal also explicitly released after save");
        await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.ConfirmedNotPublished); await Close(session);
    }
    private static StationCommand Command() => new(new(Guid.NewGuid(), 1, 1, StepKind.Move), new(Guid.NewGuid(), 1, "direct", "demo-ok-v1"), null, null);
    private static async Task IdentityAndEpochAsync()
    {
        LoopbackController controller = new() { DropNextSubmitReply = true }; controller.Model.HoldProgress = true;
        await using CommunicationStationPort port = new(controller); StationCommand command = Command();
        Check(await port.ExecuteAsync(command) is null, "direct uncertain submit returns no invented terminal");
        Check(await port.ExecuteAsync(command) is null && controller.SubmitWrites == 1, "same operation cannot allocate a second request");
        Check(await Fails(() => port.ExecuteAsync(command with { Order = command.Order with { Workpiece = "wrong" } })) && controller.SubmitWrites == 1,
            "same operation conflicting payload rejected");
        controller.Model.Reboot();
        Check(await port.ObserveAsync(command) is null && port.Snapshot.LastResolution.StartsWith("EpochChanged"), "boot change remains unknown, never done or not executed");
        Check(await port.ExecuteAsync(command) is null && controller.SubmitWrites == 1 && port.Snapshot.BootId == 1, "old operation never rebound to new boot");
        Check(await Fails(() => port.ResetObservedControlAsync()), "no terminal evidence no reset");
    }
    private static async Task NotRetainedAsync()
    {
        LoopbackController controller = new() { DropNextSubmitReply = true };
        await using CommunicationStationPort port = new(controller); StationCommand command = Command(); await port.ExecuteAsync(command);
        await UntilRemote(controller, s => s.State == ActionState.Done);
        Ticket ticket = controller.Model.Read().Current; controller.Model.SetControl(0); controller.Model.Stage(0, ticket.Encode()); controller.Model.SetControl(2); controller.Model.SetControl(0);
        Check(await port.ObserveAsync(command) is null && port.Snapshot.LastResolution.StartsWith("NotRetained"), "externally removed result cannot prove completion");
        Check(controller.SubmitWrites == 1 && !port.Snapshot.RemoteTerminalObserved && !port.Snapshot.ReadyForNextCycle, "missing result preserves original mapping and blocks new action");
        Check(await port.ObserveAsync(command with { Order = command.Order with { RecipeVersion = "demo-ng-v1" } }) is null,
            "query requires original complete station command");
    }
    private static async Task DeadlineAndCloseAsync()
    {
        LoopbackController controller = new(); controller.Model.HoldProgress = true;
        CommunicationStationPort port = new(controller); ManualStationClock clock = new();
        CommunicationStationSession session = await CommunicationStationSession.OpenCoreAsync(DirectoryName(), port, clock);
        await session.StartAsync("deadline", "demo-ng-v1"); await UntilRemote(controller, s => s.State == ActionState.Accepted);
        Check(await Fails(async () => await port.DisposeAsync()), "cannot dispose while actual network task active");
        clock.Advance(TimeSpan.FromSeconds(2)); await Until(session, s => s.Host.Station.Stage == StationStage.Faulted);
        Check(session.Snapshot.Host.TaskActive && session.Snapshot.Host.Station.Pending is not null, "phase timeout retains actual task and pending");
        session.RequestClose(); await Until(session, s => s.Host.Station.Stage == StationStage.Closing);
        Check(!await session.CloseStepAsync() && !port.Snapshot.Disposed, "close saves terminal but waits actual MC task");
        controller.Model.HoldProgress = false; await Close(session);
        Check(port.Snapshot.SubmitWrites == 1 && port.Snapshot.ReleaseWrites == 0 && session.Snapshot.Host.Station.Terminal!.Outcome == CycleOutcome.Faulted,
            "late reply drains on close, no capture, remote release or automatic recovery");
    }
    private static async Task<bool> Fails(Func<Task> work)
    { try { await work(); return false; } catch (Exception error) when (error is InvalidOperationException or InvalidDataException or ArgumentException) { return true; } }
    private static async Task Until(CommunicationStationSession session, Func<ApplicationSnapshot, bool> condition)
    {
        using CancellationTokenSource limit = new(TimeSpan.FromSeconds(10));
        while (!condition(session.Snapshot)) await Task.Delay(5, limit.Token);
    }
    private static async Task UntilRemote(LoopbackController controller, Func<IndustrialHandshake.Snapshot, bool> condition)
    {
        using CancellationTokenSource limit = new(TimeSpan.FromSeconds(10));
        while (!condition(controller.Model.Read())) await Task.Delay(5, limit.Token);
    }
    private static async Task Close(CommunicationStationSession session)
    {
        using CancellationTokenSource limit = new(TimeSpan.FromSeconds(10)); session.RequestClose();
        while (!await session.CloseStepAsync()) await Task.Delay(5, limit.Token);
    }
    public static async Task DemoAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "hnova-station-mc-" + Guid.NewGuid().ToString("N"));
        LoopbackController controller = new() { DropNextSubmitReply = true }; controller.Model.HoldProgress = true;
        CommunicationStationPort port = new(controller);
        CommunicationStationSession session = await CommunicationStationSession.OpenCoreAsync(directory, port, new ManualStationClock());
        Console.WriteLine("MC TCP LOOPBACK ONLY, no actual PLC/motion/camera/model. Files retained at " + directory);
        await session.StartAsync("lost-ack-demo", "demo-ng-v1"); await Until(session, s => s.Host.NeedsReconciliation);
        Console.WriteLine($"Lost submit ACK: stage={session.Snapshot.Host.Station.Stage}; ticket={session.Communication.BootId}/{session.Communication.RequestId}; submits={controller.SubmitWrites}");
        controller.Model.HoldProgress = false; await UntilRemote(controller, s => s.State == ActionState.Done);
        await session.ReconcileAsync(); await Until(session, s => !s.Host.TaskActive && s.Host.Station.Pending is null);
        Console.WriteLine($"Read-only reconciliation: stage={session.Snapshot.Host.Station.Stage}; submits={controller.SubmitWrites}; executions={controller.Model.Read().ExecutionCount}");
        await session.SaveResultAsync(); await session.ResetObservedControlAsync(); await session.ReleaseRemoteAfterSavedAsync();
        await session.SimulateReviewAndReleaseAsync(PeerResultDisposition.ConfirmedNotPublished); await Close(session);
        Console.WriteLine($"Saved/released/closed: releaseWrites={controller.ReleaseWrites}; closed={session.Snapshot.Closed}; portDisposed={port.Snapshot.Disposed}");
    }
}
