using IndustrialStation;

namespace IndustrialStationIntegrated;

public sealed class IntegratedStationSession
{
    private readonly StationApplication application;
    private readonly IntegratedStationPort port;
    private IntegratedStationSession(StationApplication application, IntegratedStationPort port) { this.application = application; this.port = port; }
    public static async Task<IntegratedStationSession> OpenAsync(string directory, IntegratedStationPort port, TimeProvider? clock = null)
        => new(await StationApplication.OpenWithPortAsync(directory, port, clock).ConfigureAwait(false), port);
    public ApplicationSnapshot Snapshot => application.Snapshot;
    public IntegratedPortSnapshot Devices => port.Snapshot;
    private void RequirePreparation()
    {
        ApplicationSnapshot s = application.Snapshot;
        if (s.Busy || s.CloseRequested || s.Closed || s.Host.TaskActive || s.Host.Station.Pending is not null ||
            s.Host.Station.Stage != StationStage.Idle || s.Host.Station.Order is not null || !s.Ledger.CanStartNewCycle)
            throw new InvalidOperationException("Preparation only in reviewed Idle state.");
    }
    public void OpenMotionSimulation() { RequirePreparation(); port.OpenMotionSimulation(); }
    public void EnableMotionSimulation() { RequirePreparation(); port.EnableMotionSimulation(); }
    public Task<bool> BeginReferenceSimulation() { RequirePreparation(); return port.BeginReferenceSimulation(); }
    public void OpenReplay() { RequirePreparation(); port.OpenReplay(); }
    public Task<Admission> StartAsync(string workpiece, string recipe)
    {
        IntegratedPortSnapshot d = port.Snapshot;
        return d.Motion.ReadyForNewCycle && d.Vision.ReadyForNextCycle && !d.TaskActive
            ? application.StartAsync(workpiece, recipe) : Task.FromException<Admission>(new InvalidOperationException("MotionOrVisionNotReady"));
    }
    public Task<bool> StopFlowAsync() => application.StopAsync();
    public Task<bool> SaveResultAsync() => application.SaveResultAsync();
    public Task<bool> AcknowledgeVisionAfterSavedAsync() => port.AcknowledgeVisionAfterSavedAsync(application.Snapshot);
    public Task<bool> ReconcileAsync() => application.ReconcileAsync();
    public Task<bool> SimulateReviewAndReleaseAsync(PeerResultDisposition disposition)
    {
        IntegratedPortSnapshot d = port.Snapshot;
        bool pristineVision = !d.Vision.Open && d.Vision.Workpiece is null && !d.Vision.Disposed;
        return !d.Motion.Busy && !d.TaskActive && (d.Vision.ReadyForNextCycle || pristineVision)
            ? application.SimulateReviewAndReleaseAsync(disposition) : Task.FromException<bool>(new InvalidOperationException("DeviceTaskOrVisionReceiptNotSettled"));
    }
    public void RequestClose() => application.RequestClose();
    public Task<bool> CloseStepAsync() => application.CloseStepAsync();
}
