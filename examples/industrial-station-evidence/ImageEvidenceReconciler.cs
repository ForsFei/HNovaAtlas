namespace IndustrialStationEvidence;
public enum ImageEvidenceState { Verified, Missing, StorageUnavailable, Invalid, DiagnosticConflict, BaseNotMatched, NoFrameExpected, Orphan }
public sealed record ImageEvidenceRow(Guid Session, long Cycle, ImageEvidenceState State, string Detail);
public sealed record ImageEvidenceReport(ReconciliationReport BaseReport, IReadOnlyList<ImageEvidenceRow> Images, IReadOnlyList<string> ImageStorageIssues)
{
    public bool RequiresAttention => BaseReport.RequiresAttention || ImageStorageIssues.Count != 0 ||
        Images.Any(i => i.State is not (ImageEvidenceState.Verified or ImageEvidenceState.NoFrameExpected));
}
// Offline quiescent directories only: composes the old inspection with image inspection; no cross-store transaction.
public static class ImageEvidenceReconciler
{
    public static ImageEvidenceReport Inspect(string ledger, string normal, string abnormal, string images)
    {
        ReconciliationReport basis = EvidenceReconciler.Inspect(ledger,normal,abnormal);
        List<string> issues = new(); List<ImageEvidenceRow> rows = new();
        Dictionary<(Guid,long), (AbnormalEvidence? Diagnostic,string? Error)> found = new(); bool available = true;
        try
        {
            EvidenceReconciler.RejectLink(images);
            string[] paths = Directory.EnumerateFileSystemEntries(images).Take(1001).ToArray();
            if (paths.Length > 1000) throw new InvalidDataException("Image inventory limit");
            foreach (string path in paths.Order(StringComparer.Ordinal))
            {
                string name = Path.GetFileName(path);
                if (name.EndsWith(".pending",StringComparison.Ordinal)) { issues.Add("Pending:"+name); continue; }
                if (!name.EndsWith(".json",StringComparison.Ordinal) || !EvidenceReconciler.TryIdentity(name[..^5],out Guid session,out long cycle))
                { issues.Add("UnexpectedEntry:"+name); continue; }
                try
                {
                    AbnormalImageDocument d = AbnormalImageArchive.Read(path);
                    if (d.Diagnostic.Terminal.Session != session || d.Diagnostic.Terminal.Cycle != cycle) throw new InvalidDataException("Image filename identity mismatch");
                    found.Add((session,cycle),(d.Diagnostic,null));
                }
                catch (Exception e) when (StorageError(e)) { found.Add((session,cycle),(null,e.GetType().Name)); }
            }
        }
        catch (Exception e) when (StorageError(e)) { available=false; issues.Add("ImageStorageUnavailable:"+e.GetType().Name); }
        var baseKeys = basis.Rows.Select(r=>(r.Session,r.Cycle)).ToHashSet();
        foreach (ReconciliationRow b in basis.Rows)
        {
            bool present = found.TryGetValue((b.Session,b.Cycle),out var image);
            if (b.Match != EvidenceMatch.MatchedAbnormal)
            {
                if (present) rows.Add(new(b.Session,b.Cycle,ImageEvidenceState.BaseNotMatched,"Original ledger/diagnostic match required; never fill terminal from image"));
                continue;
            }
            try
            {
                AbnormalEvidence diagnostic = AbnormalEvidenceStore.ReadRecord(Path.Combine(abnormal,AbnormalEvidenceStore.Name(b.Session,b.Cycle)));
                if (diagnostic.Terminal.Session != b.Session || diagnostic.Terminal.Cycle != b.Cycle) throw new InvalidDataException("Diagnostic changed");
                ImageEvidenceState state = !available ? ImageEvidenceState.StorageUnavailable : present && image.Error is not null ? ImageEvidenceState.Invalid :
                    present && image.Diagnostic != diagnostic ? ImageEvidenceState.DiagnosticConflict :
                    present ? ImageEvidenceState.Verified : diagnostic.DiagnosticFrame is null ? ImageEvidenceState.NoFrameExpected : ImageEvidenceState.Missing;
                rows.Add(new(b.Session,b.Cycle,state,state == ImageEvidenceState.Verified ? "Pixels and full diagnostic checked; original outcome retained" : "No automatic repair, acknowledgment or release"));
            }
            catch (Exception e) when (StorageError(e)) { rows.Add(new(b.Session,b.Cycle,ImageEvidenceState.Invalid,"Diagnostic unavailable during image comparison:"+e.GetType().Name)); }
        }
        foreach (var item in found.Where(p=>!baseKeys.Contains(p.Key))) rows.Add(new(item.Key.Item1,item.Key.Item2,ImageEvidenceState.Orphan,"Image has no matching base inventory row"));
        return new(basis,rows.AsReadOnly(),issues.AsReadOnly());
    }
    private static bool StorageError(Exception e) => e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException;
}
