using System.Threading.Channels;

namespace IndustrialVision;

public sealed record ProcessingRecipe(string Version, uint ProfileRevision, PixelRoi Region, byte Threshold);
public enum InspectionAdmission { Invalid, Accepted, Full, Closed, AlreadyTracked, Conflict, Stale }
public enum ProcessingStage { Queued, Processing, Completed, Failed }
public sealed record InspectionResult(Guid PipelineSession, CaptureRequest Request, FrameStamp Frame,
    Guid CameraEpoch, ulong TriggerSequence, ProcessingRecipe Recipe, ProcessingStage Stage,
    PixelStatistics? Measurement, string? ErrorCode);
public sealed record InspectionSubmission(InspectionAdmission Admission, Task<InspectionResult>? Result = null);
public sealed record InspectionView(ProcessingStage Stage, InspectionResult? Result);

// Managed, immutable Mono8 only. No IO, PLC verdict, retry, cancellation or native-resource ownership.
// Capacity covers queued + running + completed-but-unacknowledged entries, not just the channel.
public sealed class InspectionPipeline
{
    private sealed class Entry(CaptureResult capture)
    {
        public CaptureResult Capture { get; } = capture;
        public TaskCompletionSource<InspectionResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ProcessingStage Stage { get; set; } = ProcessingStage.Queued;
        public InspectionResult? Result { get; set; }
    }
    private readonly object gate = new();
    private readonly Channel<Entry> input;
    private readonly Dictionary<CaptureKey, Entry> retained = [];
    private readonly Guid captureOwner;
    private readonly int width, height;
    private readonly Func<Mono8Image, ProcessingRecipe, ValueTask<PixelStatistics>> measure;
    private uint highNumber;
    private bool closed;
    public Guid Session { get; } = Guid.NewGuid();
    public ProcessingRecipe Recipe { get; }
    public int Capacity { get; }
    public int RetainedCount { get { lock (gate) return retained.Count; } }
    public Task Completion { get; }

    public InspectionPipeline(Guid captureOwner, int width, int height, ProcessingRecipe recipe, int capacity = 3,
        Func<Mono8Image, ProcessingRecipe, ValueTask<PixelStatistics>>? measure = null)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (captureOwner == Guid.Empty || capacity is < 1 or > 16 || recipe.ProfileRevision == 0
            || string.IsNullOrWhiteSpace(recipe.Version) || recipe.Version.Length > 64)
            throw new ArgumentException("Owner, recipe version/profile and capacity 1..16 required.");
        _ = Mono8Image.PixelCount(width, height); recipe.Region.Validate(width, height);
        this.captureOwner = captureOwner; this.width = width; this.height = height;
        Recipe = recipe; Capacity = capacity;
        this.measure = measure ?? ((image, plan) => ValueTask.FromResult(image.Measure(plan.Region, plan.Threshold)));
        input = Channel.CreateBounded<Entry>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        Completion = Task.Run(WorkAsync); // One worker per pipeline, never Task.Run per frame.
    }
    public InspectionSubmission TrySubmit(CaptureResult capture)
    {
        // CaptureResult is a trust-boundary input: only TakeForProcessing should supply it.
        // Structural checks below do NOT prove real-camera identity, freshness or acquisition permission.
        if (capture?.Request is not { } request || capture.Evidence is not { } evidence
            || evidence.Frame?.Image is not { } image
            || request.Key.OwnerSession != captureOwner || request.Key.Number == 0
            || string.IsNullOrWhiteSpace(request.Workpiece) || request.Workpiece.Length > 80
            || request.ProfileRevision != Recipe.ProfileRevision || evidence.ProfileRevision != Recipe.ProfileRevision
            || evidence.CameraEpoch == Guid.Empty || evidence.Frame.Stamp.Session == Guid.Empty
            || evidence.Frame.Stamp.Number == 0 || evidence.TriggerSequence is null
            || !evidence.Complete || !evidence.IdentityQualified || !double.IsFinite(evidence.ReceivedAt)
            || image.Width != width || image.Height != height)
            return new(InspectionAdmission.Invalid);
        lock (gate)
        {
            if (closed) return new(InspectionAdmission.Closed);
            if (retained.TryGetValue(request.Key, out Entry? existing))
                return ReferenceEquals(existing.Capture, capture)
                    ? new(InspectionAdmission.AlreadyTracked, existing.Completion.Task)
                    : new(InspectionAdmission.Conflict);
            if (request.Key.Number <= highNumber) return new(InspectionAdmission.Stale);
            if (retained.Count == Capacity) return new(InspectionAdmission.Full);
            Entry entry = new(capture);
            retained.Add(request.Key, entry);
            if (!input.Writer.TryWrite(entry))
            {
                retained.Remove(request.Key); // No ownership transfer or high-water advance on failure.
                return new(InspectionAdmission.Full);
            }
            highNumber = request.Key.Number;
            return new(InspectionAdmission.Accepted, entry.Completion.Task);
        }
    }
    public InspectionView? Read(CaptureKey key)
    {
        lock (gate) return retained.TryGetValue(key, out Entry? entry) ? new(entry.Stage, entry.Result) : null;
    }
    public bool Acknowledge(InspectionResult result)
    {
        if (result?.Request is null || result.PipelineSession != Session) return false;
        lock (gate)
        {
            if (!retained.TryGetValue(result.Request.Key, out Entry? entry) || !ReferenceEquals(entry.Result, result)) return false;
            return retained.Remove(result.Request.Key); // Caller has handled/persisted this exact in-process result.
        }
    }
    public void Complete()
    {
        lock (gate)
        {
            if (closed) return;
            closed = true; input.Writer.TryComplete();
        }
    }
    private async Task WorkAsync()
    {
        await foreach (Entry entry in input.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            lock (gate) entry.Stage = ProcessingStage.Processing;
            PixelStatistics? statistics = null; string? errorCode = null;
            try
            {
                statistics = await measure(entry.Capture.Evidence.Frame.Image, Recipe).ConfigureAwait(false);
                if (statistics is null || statistics.Region != Recipe.Region
                    || statistics.Count != Recipe.Region.Width * Recipe.Region.Height
                    || !double.IsFinite(statistics.Mean) || statistics.Mean < statistics.Min || statistics.Mean > statistics.Max
                    || statistics.Min > statistics.Max || statistics.AtOrAboveThreshold < 0 || statistics.AtOrAboveThreshold > statistics.Count
                    || statistics.BlackPixels < 0 || statistics.BlackPixels > statistics.Count
                    || statistics.WhitePixels < 0 || statistics.WhitePixels > statistics.Count)
                    throw new InvalidOperationException("Invalid measurement summary.");
            }
            catch (Exception error)
            {
                statistics = null;
                string name = error.GetType().Name;
                errorCode = name[..Math.Min(name.Length, 80)]; // No unbounded exception message/history retention.
            }
            CaptureResult capture = entry.Capture;
            InspectionResult result = new(Session, capture.Request, capture.Evidence.Frame.Stamp,
                capture.Evidence.CameraEpoch, capture.Evidence.TriggerSequence!.Value, Recipe,
                errorCode is null ? ProcessingStage.Completed : ProcessingStage.Failed, statistics, errorCode);
            lock (gate)
            {
                entry.Stage = result.Stage; entry.Result = result; entry.Completion.SetResult(result);
            }
        }
    }
}

// Preview is deliberately a separate, lossy path. Only immutable application-owned pixels may enter.
public sealed class LatestPreviewSlot
{
    private OwnedFrame? latest;
    public bool Publish(OwnedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(frame.Image);
        if (frame.Stamp.Session == Guid.Empty || frame.Stamp.Number == 0) throw new ArgumentException("Frame identity required.");
        return Interlocked.Exchange(ref latest, frame) is not null; // True means one unconsumed preview was replaced.
    }
    public OwnedFrame? Take() => Interlocked.Exchange(ref latest, null);
}
