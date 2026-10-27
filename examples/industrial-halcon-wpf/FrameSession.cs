using IndustrialHalcon;
using IndustrialVision;

namespace IndustrialHalconWpf;

public sealed record FrameView(long Revision, Mono8Image? Image, bool Busy, bool Closing, BlobReport? Report, string? Error);

// One worker, no queue. Revision represents local display intent, not camera/workpiece identity.
public sealed class FrameSession(Func<Mono8Image, BlobReport> analyze)
{
    private readonly object gate = new();
    private Mono8Image? image;
    private long revision;
    private Task? work;
    private bool closing;
    private BlobReport? report;
    private string? error;
    public FrameView View { get { lock (gate) return new(revision, image, work is { IsCompleted: false }, closing, report, error); } }
    public bool Load(Mono8Image next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (next.Width != 8 || next.Height != 6) throw new ArgumentException("Only fixed 8x6 teaching frames supported.");
        lock (gate)
        {
            if (closing) return false;
            revision++; image = next; report = null; error = null; return true;
        }
    }
    public bool Clear()
    {
        lock (gate)
        {
            if (closing) return false;
            revision++; report = null; error = null; return true;
        }
    }
    public bool TryRun(out Task completion)
    {
        lock (gate)
        {
            if (closing || image is null || work is { IsCompleted: false }) { completion = Task.CompletedTask; return false; }
            Mono8Image captured = image; long identity = revision;
            report = null; error = null;
            work = Task.Run(() =>
            {
                BlobReport? result = null; string? failure = null;
                try
                {
                    BlobReport raw = analyze(captured);
                    // Revalidate and detach injected/backend result; no native objects cross this boundary.
                    if (raw.ImageWidth != 8 || raw.ImageHeight != 6) throw new ArgumentException("Result dimensions mismatch.");
                    result = BlobReadback.FromArrays(8, 6, raw.Candidates.Count, 2, 10,
                        raw.Candidates.Select(v => v.AreaPixels).ToArray(), raw.Candidates.Select(v => v.RowPixels).ToArray(),
                        raw.Candidates.Select(v => v.ColumnPixels).ToArray());
                    if (raw.Outcome != result.Outcome) throw new ArgumentException("Result outcome mismatch.");
                }
                catch (Exception ex) { failure = ex.GetType().Name; }
                lock (gate)
                {
                    if (!closing && revision == identity) { report = failure is null ? result : null; error = failure; }
                }
            });
            completion = work; return true;
        }
    }
    public Task CloseAsync()
    {
        lock (gate) { closing = true; report = null; error = null; return work ?? Task.CompletedTask; }
    }
}
