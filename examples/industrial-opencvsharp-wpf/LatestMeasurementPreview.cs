namespace IndustrialOpenCvSharpWpf;

public enum PreviewAdmission { Invalid, Published, Replaced, Closed }
public sealed record PreviewSnapshot(bool Pending, bool Closed, long Published, long Replaced, long Taken, long ClearedOnClose);

// One lossy slot of immutable owned image data, ordered by publication, not exposure/frame number.
public sealed class LatestMeasurementPreview
{
    private readonly object gate = new();
    private MeasurementJob? latest;
    private bool closed;
    private long published, replaced, taken, cleared;
    public PreviewSnapshot Snapshot { get { lock (gate) return new(latest is not null, closed, published, replaced, taken, cleared); } }
    public PreviewAdmission Publish(MeasurementJob job)
    {
        if (!MeasurementPipeline.ValidJob(job)) return PreviewAdmission.Invalid;
        lock (gate)
        {
            if (closed) return PreviewAdmission.Closed;
            bool replacing = latest is not null;
            latest = job; published++; if (replacing) replaced++;
            return replacing ? PreviewAdmission.Replaced : PreviewAdmission.Published;
        }
    }
    public MeasurementJob? Take()
    {
        lock (gate) { MeasurementJob? result = latest; latest = null; if (result is not null) taken++; return result; }
    }
    public void Close()
    {
        lock (gate) { closed = true; if (latest is not null) cleared++; latest = null; }
    }
}
