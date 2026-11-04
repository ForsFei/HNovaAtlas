using IndustrialStationIntegrated;
namespace IndustrialStationEvidence;

// Cooperative owner API: call all other session operations sequentially on the same owner.
public sealed class EvidenceWorkflow(IntegratedStationSession session, IntegratedStationPort port, EvidenceStore store)
{
    private int saving;
    public async Task<AbnormalEvidence> SaveAbnormalThenAcknowledgeAsync(AbnormalEvidenceStore abnormalStore)
    {
        if (Interlocked.Exchange(ref saving, 1) != 0) throw new InvalidOperationException("EvidenceSaveBusy");
        try
        {
            AbnormalEvidence record = AbnormalEvidence.Capture(session.Snapshot, session.Devices);
            await Task.Run(() => abnormalStore.Save(record)).ConfigureAwait(false);
            if (!ReferenceEquals(session.Snapshot.Host.Station.Terminal, record.Terminal)) throw new InvalidOperationException("Local terminal changed");
            if (!await session.SaveResultAsync().ConfigureAwait(false)) throw new InvalidOperationException("Terminal not saved");
            await session.AcknowledgeVisionAfterSavedAsync().ConfigureAwait(false); // false is valid when Move never created a vision cycle.
            return record;
        }
        finally { Volatile.Write(ref saving, 0); }
    }
    public async Task<EvidenceReceipt> SaveThenAcknowledgeAsync()
    {
        if (Interlocked.Exchange(ref saving, 1) != 0) throw new InvalidOperationException("EvidenceSaveBusy");
        try
        {
            var evidence = port.ExportEvidence(session.Snapshot);
            EvidenceReceipt receipt = await Task.Run(() => store.Save(evidence)).ConfigureAwait(false);
            if (!ReferenceEquals(session.Snapshot.Host.Station.Terminal, evidence.Terminal)) throw new InvalidOperationException("Local cycle changed during save");
            if (!await session.SaveResultAsync().ConfigureAwait(false)) throw new InvalidOperationException("Terminal was not saved");
            if (!await session.AcknowledgeVisionAfterSavedAsync().ConfigureAwait(false)) throw new InvalidOperationException("Original result was not acknowledged");
            return receipt;
        }
        finally { Volatile.Write(ref saving, 0); }
    }
}
