using System.Text.Json;
using System.Text.Json.Serialization;

namespace IndustrialStation;

public enum LedgerKind { Began, TerminalSaved, Reviewed }
public enum PeerResultDisposition { AcceptedByPeer, ConfirmedNotPublished }
public sealed record RestartReview(string Reviewer, string EvidenceReference, DateTimeOffset ObservedUtc,
    bool MotionIdle, bool VisionIdle, bool MaterialHandled, bool ResultChannelReconciled, PeerResultDisposition ResultDisposition);
public sealed record LedgerEntry(int Schema, long Sequence, LedgerKind Kind, Guid Session, long Cycle,
    WorkOrder Order, StationTerminal? Terminal, RestartReview? Review);
public sealed record RecordedCycle(Guid Session, long Cycle, WorkOrder Order, StationTerminal? Terminal, RestartReview? Review);
public sealed record LedgerReport(bool CanStartNewCycle, IReadOnlyList<RecordedCycle> Cycles);
internal enum CommitPoint { BeforePublish, AfterPublish }

// Trusted local directory, one cooperating writer, append-only teaching log. Not a database or device recovery engine.
public sealed class StationLedger : IDisposable
{
    private const int MaximumEntries = 1000, MaximumBytes = 65536;
    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        AllowDuplicateProperties = false,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private readonly object sync = new();
    private readonly string directory;
    private readonly FileStream writerLock;
    private readonly Action<CommitPoint>? fault;
    private readonly Dictionary<(Guid, long), RecordedCycle> cycles = new();
    private readonly Dictionary<Guid, long> highNumbers = new();
    private long sequence;
    private bool poisoned, disposed;
    private readonly bool readOnly;
    public string DirectoryPath => directory;
    public StationLedger(string directory) : this(directory, null) { }
    public static StationLedger OpenReadOnly(string directory) => new(directory, null, readOnly: true);
    internal StationLedger(string directory, Action<CommitPoint>? fault) : this(directory, fault, readOnly: false) { }
    private StationLedger(string directory, Action<CommitPoint>? fault, bool readOnly)
    {
        this.directory = Path.GetFullPath(directory); this.fault = fault; this.readOnly = readOnly;
        if (!readOnly) Directory.CreateDirectory(this.directory);
        writerLock = new(Path.Combine(this.directory, ".writer.lock"), readOnly ? FileMode.Open : FileMode.OpenOrCreate,
            readOnly ? FileAccess.Read : FileAccess.ReadWrite, readOnly ? FileShare.Read : FileShare.None);
        try { Load(); } catch { writerLock.Dispose(); throw; }
    }
    public LedgerReport Report
    {
        get
        {
            lock (sync)
            {
                EnsureOpen();
                RecordedCycle[] items = cycles.Values.ToArray();
                return new(!poisoned && items.All(c => c.Review is not null), Array.AsReadOnly(items));
            }
        }
    }
    public bool Begin(Guid session, long cycle, WorkOrder order)
    {
        lock (sync)
        {
            EnsureWritable(); ValidateIdentity(session, cycle, order);
            if (cycles.TryGetValue((session, cycle), out RecordedCycle? existing))
            {
                if (existing.Order != order) throw new InvalidDataException("Cycle identity conflict.");
                return false;
            }
            if (cycles.Values.Any(c => c.Review is null)) throw new InvalidOperationException("RestartReviewRequired");
            if (highNumbers.TryGetValue(order.Producer, out long high) && order.Number <= high)
                throw new InvalidOperationException("Durable producer number is stale.");
            Append(new(1, sequence + 1, LedgerKind.Began, session, cycle, order, null, null)); return true;
        }
    }
    public bool SaveTerminal(StationTerminal terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        lock (sync)
        {
            EnsureWritable(); RecordedCycle current = Find(terminal.Session, terminal.Cycle);
            ValidateTerminal(terminal, current);
            if (current.Terminal is not null)
            {
                if (current.Terminal != terminal) throw new InvalidDataException("Conflicting terminal.");
                return false;
            }
            if (current.Review is not null) throw new InvalidOperationException("Cycle already reviewed.");
            Append(new(1, sequence + 1, LedgerKind.TerminalSaved, terminal.Session, terminal.Cycle, current.Order, terminal, null)); return true;
        }
    }
    public bool Review(Guid session, long cycle, RestartReview evidence)
    {
        lock (sync)
        {
            EnsureWritable(); ValidateReview(evidence); RecordedCycle current = Find(session, cycle);
            if (current.Review is not null)
            {
                if (current.Review != evidence) throw new InvalidDataException("Review already sealed.");
                return false;
            }
            Append(new(1, sequence + 1, LedgerKind.Reviewed, session, cycle, current.Order, null, evidence)); return true;
        }
    }
    private void Append(LedgerEntry entry)
    {
        if (sequence >= MaximumEntries) throw new InvalidOperationException("Teaching log full; no automatic deletion.");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(entry, Json);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Record too large.");
        string temporary = Path.Combine(directory, $"{entry.Sequence:D6}.{Guid.NewGuid():N}.tmp");
        string destination = Path.Combine(directory, $"{entry.Sequence:D6}.json");
        try
        {
            using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { output.Write(bytes); output.Flush(flushToDisk: true); }
            fault?.Invoke(CommitPoint.BeforePublish);
            File.Move(temporary, destination, overwrite: false);
            fault?.Invoke(CommitPoint.AfterPublish);
            Apply(entry);
        }
        catch { poisoned = true; throw; } // Preserve evidence; caller must stop dispatch, close and inspect/reopen.
    }
    private void Load()
    {
        string[] entries = Directory.EnumerateFileSystemEntries(directory).Take(MaximumEntries + 2).ToArray();
        if (entries.Length > MaximumEntries + 1) throw new InvalidDataException("Directory exceeds teaching log bound.");
        List<string> files = new();
        foreach (string path in entries)
        {
            string name = Path.GetFileName(path);
            if (name == ".writer.lock") continue;
            if (name.EndsWith(".tmp", StringComparison.Ordinal)) throw new InvalidDataException("Uncertain temporary write; manual storage inspection required.");
            if (Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
                name.Length != 11 || !name.EndsWith(".json", StringComparison.Ordinal) || !name[..6].All(char.IsAsciiDigit))
                throw new InvalidDataException("Unexpected log entry.");
            files.Add(path);
        }
        foreach (string path in files.Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(path) != $"{sequence + 1:D6}.json") throw new InvalidDataException("Log sequence gap.");
            using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length is <= 0 or > MaximumBytes) throw new InvalidDataException("Invalid record length.");
            byte[] bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes);
            LedgerEntry entry = JsonSerializer.Deserialize<LedgerEntry>(bytes, Json) ?? throw new InvalidDataException("Null record.");
            Apply(entry);
        }
    }
    private void Apply(LedgerEntry entry)
    {
        if (entry.Schema != 1 || entry.Sequence != sequence + 1 || !Enum.IsDefined(entry.Kind)) throw new InvalidDataException("Unsupported schema/sequence/kind.");
        ValidateIdentity(entry.Session, entry.Cycle, entry.Order);
        (Guid, long) key = (entry.Session, entry.Cycle);
        if (entry.Kind == LedgerKind.Began)
        {
            if (entry.Terminal is not null || entry.Review is not null || cycles.ContainsKey(key) || cycles.Values.Any(c => c.Review is null) ||
                highNumbers.TryGetValue(entry.Order.Producer, out long high) && entry.Order.Number <= high)
                throw new InvalidDataException("Invalid Begin order or duplicate.");
            cycles.Add(key, new(entry.Session, entry.Cycle, entry.Order, null, null)); highNumbers[entry.Order.Producer] = entry.Order.Number;
        }
        else
        {
            RecordedCycle current = Find(entry.Session, entry.Cycle);
            if (current.Order != entry.Order || current.Review is not null) throw new InvalidDataException("Order mismatch or already reviewed.");
            if (entry.Kind == LedgerKind.TerminalSaved)
            {
                if (entry.Terminal is null || entry.Review is not null || current.Terminal is not null) throw new InvalidDataException("Invalid terminal entry.");
                ValidateTerminal(entry.Terminal, current); cycles[key] = current with { Terminal = entry.Terminal };
            }
            else
            {
                if (entry.Terminal is not null || entry.Review is null) throw new InvalidDataException("Invalid review entry.");
                ValidateReview(entry.Review); cycles[key] = current with { Review = entry.Review };
            }
        }
        sequence = entry.Sequence;
    }
    private RecordedCycle Find(Guid session, long cycle) => cycles.TryGetValue((session, cycle), out RecordedCycle? value)
        ? value : throw new InvalidDataException("Unknown recorded cycle.");
    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);
    private static void ValidateIdentity(Guid session, long cycle, WorkOrder? order)
    {
        if (session == Guid.Empty || cycle <= 0 || order is null || order.Producer == Guid.Empty || order.Number <= 0 ||
            !Text(order.Workpiece) || !Text(order.RecipeVersion)) throw new InvalidDataException("Invalid cycle identity.");
    }
    private static void ValidateTerminal(StationTerminal terminal, RecordedCycle current)
    {
        if (terminal.Session != current.Session || terminal.Cycle != current.Cycle || terminal.Order != current.Order ||
            !Enum.IsDefined(terminal.Outcome) || !Text(terminal.Reason) || terminal.FrameId == Guid.Empty ||
            terminal.Judgment is { } verdict && !Enum.IsDefined(verdict) ||
            terminal.Judgment is not null && terminal.FrameId is null ||
            terminal.Outcome == CycleOutcome.Completed && (terminal.FrameId is null || terminal.Judgment is null))
            throw new InvalidDataException("Invalid terminal payload or identity.");
    }
    private static void ValidateReview(RestartReview? review)
    {
        if (review is null || !Text(review.Reviewer) || !Text(review.EvidenceReference) || review.ObservedUtc == default ||
            !review.MotionIdle || !review.VisionIdle || !review.MaterialHandled || !review.ResultChannelReconciled ||
            !Enum.IsDefined(review.ResultDisposition)) throw new InvalidDataException("Incomplete explicit reconciliation evidence.");
    }
    private void EnsureOpen() => ObjectDisposedException.ThrowIf(disposed, this);
    private void EnsureWritable() { EnsureOpen(); if (readOnly) throw new InvalidOperationException("ReadOnlyLedger"); if (poisoned) throw new InvalidOperationException("StorageUncertain: stop dispatch and inspect/reopen."); }
    public void Dispose() { lock (sync) { if (disposed) return; disposed = true; writerLock.Dispose(); } }
}
