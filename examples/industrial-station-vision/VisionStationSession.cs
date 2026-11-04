using IndustrialStation;

namespace IndustrialStationVision;

public sealed class VisionStationSession
{
    private readonly StationApplication application;
    private readonly VisionStationPort port;
    private VisionStationSession(StationApplication application, VisionStationPort port) { this.application = application; this.port = port; }
    public static async Task<VisionStationSession> OpenAsync(string directory, VisionStationPort port, TimeProvider? clock = null)
        => new(await StationApplication.OpenWithPortAsync(directory, port, clock).ConfigureAwait(false), port);
    public ApplicationSnapshot Snapshot => application.Snapshot;
    public VisionPortSnapshot Vision => port.Snapshot;
    public void OpenReplay()
    {
        ApplicationSnapshot s = application.Snapshot;
        if (s.Busy || s.CloseRequested || s.Closed || s.Host.Station.Stage != StationStage.Idle || !s.Ledger.CanStartNewCycle)
            throw new InvalidOperationException("Open replay only in reviewed Idle state.");
        port.OpenReplay();
    }
    public Task<Admission> StartAsync(string workpiece, string recipe) => port.Snapshot.ReadyForNextCycle ? application.StartAsync(workpiece, recipe)
        : Task.FromException<Admission>(new InvalidOperationException("VisionSlotNotReady"));
    public Task<bool> SaveResultAsync() => application.SaveResultAsync();
    public Task<bool> AcknowledgeVisionAfterSavedAsync() => port.AcknowledgeAfterSavedAsync(application.Snapshot);
    public Task<bool> ReconcileAsync() => application.ReconcileAsync();
    public Task<bool> SimulateReviewAndReleaseAsync(PeerResultDisposition disposition)
    {
        VisionPortSnapshot s = port.Snapshot;
        bool untouchedReplay = !s.Open && s.Workpiece is null && !s.TaskActive && !s.Disposed;
        return s.ReadyForNextCycle || untouchedReplay ? application.SimulateReviewAndReleaseAsync(disposition)
            : Task.FromException<bool>(new InvalidOperationException("VisionReceiptNotAcknowledged"));
    }
    public void RequestClose() => application.RequestClose();
    public Task<bool> CloseStepAsync() => application.CloseStepAsync();
}
