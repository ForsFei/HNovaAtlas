using IndustrialStation;

namespace IndustrialStationMotion;

public sealed class MotionStationSession
{
    private readonly StationApplication application;
    private readonly MotionStationPort port;
    private MotionStationSession(StationApplication application, MotionStationPort port) { this.application = application; this.port = port; }
    public static async Task<MotionStationSession> OpenAsync(string directory, MotionStationPort port, TimeProvider? clock = null)
    { return new(await StationApplication.OpenWithPortAsync(directory, port, clock).ConfigureAwait(false), port); }
    public ApplicationSnapshot Snapshot => application.Snapshot;
    public MotionPortSnapshot Motion => port.Snapshot;
    public void OpenSimulation() { RequireMaintenance(); port.OpenSimulation(); }
    public void EnableSimulation() { RequireMaintenance(); port.EnableSimulation(); }
    public Task<bool> BeginReferenceSimulation() { RequireMaintenance(); return port.BeginReferenceSimulation(); }
    public void ResetSimulationFault() { RequireMaintenance(); port.ResetSimulationFault(); }
    private void RequireMaintenance()
    {
        ApplicationSnapshot s = application.Snapshot;
        if (s.Busy || s.CloseRequested || s.Closed || s.Host.TaskActive || s.Host.Station.Pending is not null ||
            s.Host.Station.Stage != StationStage.Idle || s.Host.Station.Order is not null || !s.Ledger.CanStartNewCycle)
            throw new InvalidOperationException("Preparation only outside an admitted/review-required cycle.");
    }
    public Task<Admission> StartAsync(string workpiece, string recipe) => port.Snapshot.ReadyForNewCycle
        ? application.StartAsync(workpiece, recipe) : Task.FromException<Admission>(new InvalidOperationException("MotionReferenceNotReady"));
    public Task<bool> StopFlowAsync() => application.StopAsync();
    public Task<bool> SaveResultAsync() => application.SaveResultAsync();
    public Task<bool> ReconcileAsync() => application.ReconcileAsync();
    public Task<bool> SimulateReviewAndReleaseAsync(PeerResultDisposition disposition) => !port.Snapshot.Busy
        ? application.SimulateReviewAndReleaseAsync(disposition) : Task.FromException<bool>(new InvalidOperationException("AxisStillBusy"));
    public void RequestClose() => application.RequestClose();
    public Task<bool> CloseStepAsync() => application.CloseStepAsync();
}
