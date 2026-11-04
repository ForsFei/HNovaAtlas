using IndustrialStation;

namespace IndustrialStationCommunication;

// Minimal application facade: remote result release is an additional prerequisite for local release/new admission.
public sealed class CommunicationStationSession
{
    private readonly StationApplication application;
    private readonly CommunicationStationPort port;
    private CommunicationStationSession(StationApplication application, CommunicationStationPort port)
    { this.application = application; this.port = port; }
    public static Task<CommunicationStationSession> OpenAsync(string directory) => OpenCoreAsync(directory, CommunicationStationPort.CreateLoopback(), null);
    internal static async Task<CommunicationStationSession> OpenCoreAsync(string directory, CommunicationStationPort port, TimeProvider? clock)
    {
        StationApplication application = await StationApplication.OpenWithPortAsync(directory, port, clock).ConfigureAwait(false);
        return new(application, port);
    }
    public ApplicationSnapshot Snapshot => application.Snapshot;
    public CommunicationPortSnapshot Communication => port.Snapshot;
    internal StationApplication Application => application;
    public Task<Admission> StartAsync(string workpiece, string recipeVersion) => port.Snapshot.ReadyForNextCycle
        ? application.StartAsync(workpiece, recipeVersion)
        : Task.FromException<Admission>(new InvalidOperationException("RemoteResultNotReleased"));
    public Task<bool> SaveResultAsync() => application.SaveResultAsync();
    public Task<bool> ReconcileAsync() => application.ReconcileAsync();
    public Task<bool> ResetObservedControlAsync() => port.ResetObservedControlAsync();
    public Task<bool> ReleaseRemoteAfterSavedAsync() => port.ReleaseAfterSavedAsync(application.Snapshot);
    public Task<bool> ObserveReleaseAsync() => port.ObserveReleaseAsync();
    public Task<bool> SimulateReviewAndReleaseAsync(PeerResultDisposition disposition) => port.Snapshot.ReadyForNextCycle
        ? application.SimulateReviewAndReleaseAsync(disposition)
        : Task.FromException<bool>(new InvalidOperationException("RemoteResultNotReleased"));
    public void RequestClose() => application.RequestClose();
    public Task<bool> CloseStepAsync() => application.CloseStepAsync();
}
