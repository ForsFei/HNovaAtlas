using System.Threading.Channels;
using IndustrialYolo.Images;
using IndustrialYolo.Onnx;

namespace IndustrialYolo.Wpf;

public sealed record YoloWork(Guid Producer, long Number, ImageSnapshot Image);
public enum YoloAdmission { Accepted, Starting, Invalid, AlreadyTracked, Conflict, Stale, Full, Closed }
public enum YoloStage { Queued, Running, Completed, Failed }
public sealed record YoloTerminal(Guid Session, YoloWork Work, YoloStage Stage, OverlayResult? Output, string? Error);
public sealed record YoloSubmission(YoloAdmission Admission, Task<YoloTerminal>? Completion = null);
public sealed record WorkerSnapshot(int Capacity, int Retained, int Queued, int Running, int Unacknowledged,
    bool Ready, bool Closed, bool Faulted, long Accepted, long Completed, long Failed, long Acknowledged);

internal interface IYoloEngine : IDisposable { OverlayResult Process(ImageSnapshot image); }

// Created, used and disposed by ONE logical worker. Not tied to one OS thread.
internal sealed class ReusedYoloEngine : IYoloEngine
{
    private readonly YoloXNanoSession session;
    private readonly float score;
    internal int Runs { get; private set; }
    internal bool Disposed { get; private set; }
    public ReusedYoloEngine(string model, float score)
    { session = new(model); this.score = score; }
    public OverlayResult Process(ImageSnapshot image)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        PreparedImage prepared = OpenCvImageInput.Prepare(image.Source);
        Runs++;
        YoloXRawOutput raw = session.Run(prepared.Tensor);
        return OverlayResult.FromReport(image, ImagePostprocessor.Process(raw, prepared, new(score, .45)));
    }
    public void Dispose() { if (Disposed) return; Disposed = true; session.Dispose(); }
}

public sealed class YoloInferenceWorker
{
    private sealed class Entry(YoloWork work)
    {
        public YoloWork Work { get; } = work;
        public YoloStage Stage { get; set; } = YoloStage.Queued;
        public YoloTerminal? Terminal { get; set; }
        public TaskCompletionSource<YoloTerminal> Promise { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly object gate = new();
    private readonly Channel<Entry> input;
    private readonly Dictionary<long, Entry> retained = [];
    private readonly Func<IYoloEngine> factory;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long high, accepted, completed, failed, acknowledged;
    private bool initialized, closed, faulted;
    public Guid Producer { get; } = Guid.NewGuid();
    public Guid Session { get; } = Guid.NewGuid();
    public int Capacity { get; }
    public Task Ready => ready.Task;
    public Task Completion { get; }
    public WorkerSnapshot Snapshot
    {
        get
        {
            lock (gate) return new(Capacity, retained.Count, retained.Values.Count(e => e.Stage == YoloStage.Queued),
                retained.Values.Count(e => e.Stage == YoloStage.Running), retained.Values.Count(e => e.Terminal is not null),
                initialized, closed, faulted, accepted, completed, failed, acknowledged);
        }
    }
    public YoloInferenceWorker(string modelPath, int capacity = 2, float score = .1f)
        : this(CreateFactory(modelPath, score), capacity) { }
    private static Func<IYoloEngine> CreateFactory(string path, float score)
    {
        if (string.IsNullOrWhiteSpace(path) || !float.IsFinite(score) || score is < 0 or > 1) throw new ArgumentException("Model path and finite0..1 score required.");
        string fullPath = System.IO.Path.GetFullPath(path);
        return () => new ReusedYoloEngine(fullPath, score);
    }
    // Only tests in this assembly inject a fake. Public construction always uses real CPU ONNX.
    internal YoloInferenceWorker(Func<IYoloEngine> factory, int capacity)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (capacity is < 1 or > 8) throw new ArgumentException("Teaching capacity must be1..8.");
        this.factory = factory; Capacity = capacity;
        input = Channel.CreateBounded<Entry>(new BoundedChannelOptions(capacity)
        { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        Completion = Task.Run(WorkAsync);
    }
    public YoloSubmission TrySubmit(YoloWork work)
    {
        if (work is null || work.Producer != Producer || work.Number <= 0 || work.Image is null) return new(YoloAdmission.Invalid);
        lock (gate)
        {
            if (closed) return new(YoloAdmission.Closed);
            if (!initialized) return new(YoloAdmission.Starting);
            if (retained.TryGetValue(work.Number, out Entry? existing))
                return ReferenceEquals(work, existing.Work) ? new(YoloAdmission.AlreadyTracked, existing.Promise.Task) : new(YoloAdmission.Conflict);
            if (work.Number <= high) return new(YoloAdmission.Stale);
            if (retained.Count >= Capacity) return new(YoloAdmission.Full);
            Entry entry = new(work); retained.Add(work.Number, entry);
            if (!input.Writer.TryWrite(entry)) { retained.Remove(work.Number); return new(YoloAdmission.Full); }
            high = work.Number; accepted++;
            return new(YoloAdmission.Accepted, entry.Promise.Task);
        }
    }
    public YoloTerminal? ReadTerminal(long number) { lock (gate) return retained.GetValueOrDefault(number)?.Terminal; }
    public bool Acknowledge(YoloTerminal terminal)
    {
        if (terminal is null || terminal.Work is null) return false;
        lock (gate)
        {
            if (terminal.Session != Session || !retained.TryGetValue(terminal.Work.Number, out Entry? entry) ||
                !ReferenceEquals(entry.Terminal, terminal)) return false;
            retained.Remove(terminal.Work.Number); acknowledged++; return true;
        }
    }
    public void Complete() { lock (gate) { closed = true; input.Writer.TryComplete(); } }
    private void Finish(Entry entry, OverlayResult? output, string? error)
    {
        lock (gate)
        {
            entry.Stage = error is null ? YoloStage.Completed : YoloStage.Failed;
            entry.Terminal = new(Session, entry.Work, entry.Stage, output, error);
            if (error is null) completed++; else failed++;
            entry.Promise.SetResult(entry.Terminal);
        }
    }
    private async Task WorkAsync()
    {
        IYoloEngine? engine = null; Exception? fatal = null;
        try
        {
            engine = factory() ?? throw new InvalidOperationException("Engine factory returned null.");
            lock (gate) initialized = true;
            ready.TrySetResult();
            await foreach (Entry entry in input.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                lock (gate) entry.Stage = YoloStage.Running;
                try
                {
                    OverlayResult output = engine.Process(entry.Work.Image);
                    if (!ReferenceEquals(output.Image, entry.Work.Image)) throw new InvalidOperationException("Cross-image engine output.");
                    Finish(entry, output, null);
                }
                catch (Exception error)
                {
                    // Close admission BEFORE publishing failure: a fast consumer may acknowledge immediately.
                    lock (gate) { faulted = true; closed = true; input.Writer.TryComplete(); }
                    Finish(entry, null, $"ProcessingFailed:{error.GetType().Name}"); throw;
                }
            }
        }
        catch (Exception error)
        {
            fatal = error;
            lock (gate) { faulted = true; closed = true; input.Writer.TryComplete(); }
            ready.TrySetException(error);
            while (input.Reader.TryRead(out Entry? waiting)) Finish(waiting, null, "WorkerStoppedBeforeRun");
        }
        finally
        {
            try { engine?.Dispose(); }
            catch (Exception error) { fatal = fatal is null ? error : new AggregateException(fatal, error); }
            lock (gate) { closed = true; faulted |= fatal is not null; }
        }
        if (fatal is not null) throw new InvalidOperationException("Worker stopped; inspect terminal receipts and inner error. No automatic retry.", fatal);
    }
}
