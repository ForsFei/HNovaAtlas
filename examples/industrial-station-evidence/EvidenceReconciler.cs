using IndustrialStation;
namespace IndustrialStationEvidence;

public enum EvidenceMatch { MatchedNormal, MatchedAbnormal, IntentWithoutTerminal, EvidenceWithoutTerminal,
    EvidenceWithoutIntent, MissingEvidence, TerminalConflict, InvalidEvidence, DuplicateEvidence }
public sealed record ReconciliationRow(Guid Session, long Cycle, EvidenceMatch Match, bool ReviewRecorded, string Detail);
public sealed record ReconciliationReport(IReadOnlyList<ReconciliationRow> Rows, IReadOnlyList<string> StorageIssues)
{
    public bool RequiresAttention => StorageIssues.Count != 0 || Rows.Any(r => !r.ReviewRecorded || r.Match is not (EvidenceMatch.MatchedNormal or EvidenceMatch.MatchedAbnormal));
}

public static class EvidenceReconciler
{
    private sealed record Found(StationTerminal? Terminal, bool Abnormal, string? Error);
    // Existing lock files opened read-only; no create, review, rename, cleanup, device/task/API action.
    public static ReconciliationReport Inspect(string ledgerDirectory, string normalDirectory, string abnormalDirectory)
    {
        List<ReconciliationRow> rows = new(); List<string> issues = new();
        Dictionary<(Guid, long), List<Found>> evidence = new();
        StationLedger ledger;
        try { RejectLink(ledgerDirectory); RejectLink(Path.Combine(ledgerDirectory, ".writer.lock")); ledger = StationLedger.OpenReadOnly(ledgerDirectory); }
        catch (Exception e) when (StorageError(e)) { issues.Add("LedgerUnavailable:" + e.GetType().Name); return new(rows.AsReadOnly(), issues.AsReadOnly()); }
        List<FileStream> heldLocks = new();
        try
        {
            using (ledger)
            {
                Scan(normalDirectory, false); Scan(abnormalDirectory, true);
                var records = ledger.Report.Cycles.ToDictionary(c => (c.Session, c.Cycle));
                foreach (var key in records.Keys.Union(evidence.Keys).OrderBy(k => k.Item1).ThenBy(k => k.Item2))
                {
                    records.TryGetValue(key, out RecordedCycle? c); evidence.TryGetValue(key, out List<Found>? found);
                    EvidenceMatch match; string detail;
                    if (found?.Count > 1) { match = EvidenceMatch.DuplicateEvidence; detail = "Multiple evidence records for one cycle"; }
                    else if (found is [ { Error: not null } bad ]) { match = EvidenceMatch.InvalidEvidence; detail = bad.Error!; }
                    else if (c is null) { match = EvidenceMatch.EvidenceWithoutIntent; detail = "No durable Began record"; }
                    else if (c.Terminal is null) { match = found is null ? EvidenceMatch.IntentWithoutTerminal : EvidenceMatch.EvidenceWithoutTerminal; detail = "Original terminal unknown; never reconstruct from evidence"; }
                    else if (found is null) { match = EvidenceMatch.MissingEvidence; detail = "Terminal has no published evidence"; }
                    else if (found[0].Terminal != c.Terminal) { match = EvidenceMatch.TerminalConflict; detail = "Ledger and evidence terminal differ"; }
                    else { match = found[0].Abnormal ? EvidenceMatch.MatchedAbnormal : EvidenceMatch.MatchedNormal; detail = found[0].Abnormal ? "Diagnostic only; no image bytes" : "Original image and measurements verified"; }
                    rows.Add(new(key.Item1, key.Item2, match, c?.Review is not null, detail));
                }
            }
            return new(rows.AsReadOnly(), issues.AsReadOnly());
        }
        finally { foreach (FileStream hold in heldLocks) hold.Dispose(); }
        void Scan(string directory, bool abnormal)
        {
            try
            {
                RejectLink(directory); string lockPath = Path.Combine(directory, ".writer.lock"); RejectLink(lockPath);
                FileStream hold = new(lockPath, FileMode.Open, FileAccess.Read, FileShare.Read); heldLocks.Add(hold);
                string[] paths = Directory.EnumerateFileSystemEntries(directory).Take(1002).Order(StringComparer.Ordinal).ToArray();
                if (paths.Length > 1001) throw new InvalidDataException("Evidence inventory limit exceeded");
                foreach (string path in paths)
                {
                    string name = Path.GetFileName(path); if (name == ".writer.lock") continue;
                    if (name.EndsWith(".pending", StringComparison.Ordinal)) { issues.Add("Pending:" + name); continue; }
                    string stem = abnormal && name.EndsWith(".json", StringComparison.Ordinal) ? name[..^5] : name;
                    if (!TryIdentity(stem, out Guid session, out long cycle) || abnormal && stem == name)
                    { issues.Add("UnexpectedEntry:" + name); continue; }
                    Found entry;
                    try
                    {
                        RejectLink(path);
                        StationTerminal terminal;
                        if (abnormal)
                        {
                            AbnormalEvidence r = AbnormalEvidenceStore.ReadRecord(path); terminal = r.Terminal;
                            if (terminal.Session != session || terminal.Cycle != cycle) throw new InvalidDataException("Filename identity mismatch");
                        }
                        else terminal = EvidenceStore.ReadBundle(directory, session, cycle).Manifest.Terminal;
                        entry = new(terminal, abnormal, null);
                    }
                    catch (Exception e) when (StorageError(e)) { entry = new(null, abnormal, e.GetType().Name); }
                    if (!evidence.TryGetValue((session, cycle), out List<Found>? list)) evidence.Add((session, cycle), list = new());
                    list.Add(entry);
                }
            }
            catch (Exception e) when (StorageError(e)) { issues.Add((abnormal ? "Abnormal" : "Normal") + "StorageUnavailable:" + e.GetType().Name); }
        }
    }
    internal static bool TryIdentity(string name, out Guid session, out long cycle)
    {
        session = Guid.Empty; cycle = 0;
        if (name.Length < 43 || name[32] != '-' || !Guid.TryParseExact(name[..32], "N", out session) || session == Guid.Empty ||
            !long.TryParse(name[33..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out cycle) || cycle <= 0) return false;
        return name == session.ToString("N") + "-" + cycle.ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
    }
    internal static void RejectLink(string path)
    { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse point rejected"); }
    private static bool StorageError(Exception e) => e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException;
}
