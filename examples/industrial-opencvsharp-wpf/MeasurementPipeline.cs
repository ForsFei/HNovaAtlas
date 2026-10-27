using System.Threading.Channels;
using IndustrialOpenCvSharp;

namespace IndustrialOpenCvSharpWpf;

public sealed record MeasurementJob(Guid Producer, ulong Number, Guid FrameId, FrameSelection Selection);
public enum PipelineAdmission { Invalid, Accepted, AlreadyTracked, Conflict, Stale, Full, Closed }
public enum PipelineStage { Queued, Running, Completed, Failed }
public sealed record MeasurementTerminal(Guid Session, string RecipeVersion, MeasurementJob Job,
    PipelineStage Stage, RenderedMeasurement? Output, string? Error)
{
    // Correlation only. Caller separately checks Completed/WidthMeasured; not authentication or acceptance.
    public bool MatchesDisplay(MeasurementJob shown, Guid expectedSession) => expectedSession != Guid.Empty &&
        Session == expectedSession && ReferenceEquals(Job, shown);
}
public sealed record PipelineSubmission(PipelineAdmission Admission, Task<MeasurementTerminal>? Completion = null);
public sealed record PipelineEntryView(PipelineStage Stage, MeasurementTerminal? Terminal);
public sealed record PipelineSnapshot(int Capacity, int Retained, int Queued, int Running, int Unacknowledged,
    bool Closed, long Accepted, long Completed, long Failed, long Acknowledged);

// Bounded ledger covers queued + running + ALL unacknowledged terminals. No native objects stored here.
public sealed class MeasurementPipeline
{
    private sealed class Entry(MeasurementJob job)
    {
        public MeasurementJob Job { get; } = job;
        public PipelineStage Stage { get; set; } = PipelineStage.Queued;
        public MeasurementTerminal? Terminal { get; set; }
        public TaskCompletionSource<MeasurementTerminal> Promise { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly object gate = new();
    private readonly Channel<Entry> input;
    private readonly Dictionary<ulong, Entry> retained = [];
    private readonly Func<FrameSelection, RenderedMeasurement> analyze;
    private readonly Guid producer;
    private readonly int width, height;
    private ulong highNumber;
    private bool closed;
    private long accepted, completed, failed, acknowledged;
    public Guid Session { get; } = Guid.NewGuid();
    public string RecipeVersion { get; }
    public StripeRecipe Recipe { get; }
    public int Capacity { get; }
    public Task Completion { get; }
    public PipelineSnapshot Snapshot
    {
        get
        {
            lock (gate) return new(Capacity, retained.Count,
                retained.Values.Count(e => e.Stage == PipelineStage.Queued),
                retained.Values.Count(e => e.Stage == PipelineStage.Running),
                retained.Values.Count(e => e.Terminal is not null), closed, accepted, completed, failed, acknowledged);
        }
    }
    public MeasurementPipeline(Guid producer, int width, int height, StripeRecipe recipe, string version,
        int capacity = 2, Func<FrameSelection, RenderedMeasurement>? analyze = null)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (producer == Guid.Empty || string.IsNullOrWhiteSpace(version) || version.Length > 64 || capacity is < 1 or > 8)
            throw new ArgumentException("Producer, bounded version and teaching capacity1..8 required.");
        recipe.ValidateImage(width, height);
        this.producer = producer; this.width = width; this.height = height;
        Recipe = recipe; RecipeVersion = version; Capacity = capacity;
        this.analyze = analyze ?? NativeFrameRunner.Analyze;
        input = Channel.CreateBounded<Entry>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        Completion = Task.Run(WorkAsync); // ONE logical consumer, not one Task.Run per image.
    }
    internal static bool ValidJob(MeasurementJob? job)
    {
        if (job is not { Producer: var p, Number: > 0, FrameId: var f, Selection: { Image: { } image, Recipe: { } recipe } selection } ||
            p == Guid.Empty || f == Guid.Empty || string.IsNullOrWhiteSpace(selection.Name) || selection.Name.Length > 64) return false;
        try { recipe.ValidateImage(image.Width, image.Height); return true; }
        catch (ArgumentException) { return false; }
    }
    public PipelineSubmission TrySubmit(MeasurementJob job)
    {
        if (!ValidJob(job) || job.Producer != producer || job.Selection.Image.Width != width ||
            job.Selection.Image.Height != height || job.Selection.Recipe != Recipe) return new(PipelineAdmission.Invalid);
        lock (gate)
        {
            if (closed) return new(PipelineAdmission.Closed);
            if (retained.TryGetValue(job.Number, out Entry? existing))
                return ReferenceEquals(existing.Job, job) ? new(PipelineAdmission.AlreadyTracked, existing.Promise.Task) : new(PipelineAdmission.Conflict);
            if (job.Number <= highNumber) return new(PipelineAdmission.Stale);
            if (retained.Count >= Capacity) return new(PipelineAdmission.Full);
            Entry entry = new(job); retained.Add(job.Number, entry);
            if (!input.Writer.TryWrite(entry))
            {
                retained.Remove(job.Number); return new(PipelineAdmission.Full); // No transfer/high-water advance.
            }
            highNumber = job.Number; accepted++;
            return new(PipelineAdmission.Accepted, entry.Promise.Task);
        }
    }
    public PipelineEntryView? Read(ulong number)
    {
        lock (gate) return retained.TryGetValue(number, out Entry? entry) ? new(entry.Stage, entry.Terminal) : null;
    }
    public bool Acknowledge(MeasurementTerminal terminal)
    {
        if (terminal is not { Job: { } job } || terminal.Session != Session) return false;
        lock (gate)
        {
            if (!retained.TryGetValue(job.Number, out Entry? entry) || !ReferenceEquals(entry.Terminal, terminal)) return false;
            retained.Remove(job.Number); acknowledged++; return true;
        }
    }
    public void Complete()
    {
        lock (gate) { if (closed) return; closed = true; input.Writer.TryComplete(); }
    }
    private async Task WorkAsync()
    {
        await foreach (Entry entry in input.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            lock (gate) entry.Stage = PipelineStage.Running;
            RenderedMeasurement? output = null; string? error = null;
            try { output = FrameSession.Detach(analyze(entry.Job.Selection), entry.Job.Selection); }
            catch (Exception ex)
            {
                while (ex is TypeInitializationException && ex.InnerException is { } inner) ex = inner;
                string name = ex.GetType().Name; error = name[..Math.Min(name.Length, 80)];
            }
            MeasurementTerminal terminal = new(Session, RecipeVersion, entry.Job,
                error is null ? PipelineStage.Completed : PipelineStage.Failed, output, error);
            lock (gate)
            {
                entry.Stage = terminal.Stage; entry.Terminal = terminal;
                if (error is null) completed++; else failed++;
                entry.Promise.SetResult(terminal);
            }
        }
    }
}
