using System.Windows.Media;
using System.Windows.Media.Imaging;
using IndustrialOpenCvSharp;
using IndustrialVision;

namespace IndustrialOpenCvSharpWpf;

public sealed record FrameSelection(string Name, Mono8Image Image, StripeRecipe Recipe);
public sealed record FrameView(long Revision, FrameSelection? Selected, BitmapSource? Bitmap,
    bool Busy, bool Closing, StripeReport? Report, string? Error);

public static class FrameFactory
{
    public static FrameSelection Create(int index)
    {
        if (index is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(index));
        bool dark = index == 1;
        byte[] pixels = new byte[128 * 96];
        for (int y = 0; y < 96; y++)
            for (int x = 0; x < 128; x++)
            {
                bool inside = index != 3 && (x is >= 40 and < 72 || index == 2 && x is >= 88 and < 96);
                pixels[y * 128 + x] = (byte)(inside ? dark ? 20 : 220 : dark ? 220 : 20);
            }
        return new(new[] { "亮条", "暗槽", "双条带", "空白" }[index], Mono8Image.FromPacked(128, 96, pixels),
            new(new(16, 16, 96, 64), Polarity: dark ? StripePolarity.Dark : StripePolarity.Bright));
    }
}

// One worker, no queue. Revision is LOCAL display intent, not camera or workpiece identity.
public sealed class FrameSession(Func<FrameSelection, RenderedMeasurement> analyze)
{
    private readonly object gate = new();
    private FrameSelection? selected;
    private BitmapSource? bitmap;
    private StripeReport? report;
    private string? error;
    private long revision;
    private Task? work;
    private bool closing;
    public FrameView View { get { lock (gate) return new(revision, selected, bitmap, work is { IsCompleted: false }, closing, report, error); } }
    public bool Load(FrameSelection next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (string.IsNullOrWhiteSpace(next.Name) || next.Name.Length > 64) throw new ArgumentException("Bounded display name required.");
        next.Recipe.ValidateImage(next.Image.Width, next.Image.Height);
        lock (gate)
        {
            if (closing) return false;
            BitmapSource preview = BitmapBridge.FromOwned(next.Image);
            revision = checked(revision + 1); selected = next; bitmap = preview; report = null; error = null; return true;
        }
    }
    public bool Clear()
    {
        lock (gate)
        {
            if (closing) return false;
            revision = checked(revision + 1); report = null; error = null; return true;
        }
    }
    internal static RenderedMeasurement Detach(RenderedMeasurement output, FrameSelection source)
    {
        BitmapSource b = output.Bitmap;
        if (!b.IsFrozen || b.Format != PixelFormats.Gray8 || b.PixelWidth != source.Image.Width || b.PixelHeight != source.Image.Height)
            throw new ArgumentException("Expected frozen matching Gray8 bitmap.");
        byte[] pixels = new byte[source.Image.Width * source.Image.Height];
        b.CopyPixels(pixels, source.Image.Width, 0);
        if (!pixels.SequenceEqual(source.Image.CopyPixels())) throw new ArgumentException("Bitmap does not match captured input.");
        StripeReport raw = output.Report;
        if (raw.Recipe != source.Recipe || raw.MeanProfile.Count != source.Recipe.Length || raw.SmoothedProfile.Count != source.Recipe.Length)
            throw new ArgumentException("Report recipe/profile envelope mismatch.");
        // Report profiles are scan-ordered; Analyze expects positive-axis input and reverses internally.
        double[] mean = raw.MeanProfile.ToArray(), smooth = raw.SmoothedProfile.ToArray();
        if (source.Recipe.Reverse) { Array.Reverse(mean); Array.Reverse(smooth); }
        StripeReport detached = StripeAnalysis.Analyze(mean, smooth, source.Recipe);
        if (raw.Outcome != detached.Outcome || raw.WidthPixels != detached.WidthPixels ||
            raw.FirstImagePoint != detached.FirstImagePoint || raw.SecondImagePoint != detached.SecondImagePoint ||
            !raw.Edges.SequenceEqual(detached.Edges) || !raw.Pairs.SequenceEqual(detached.Pairs))
            throw new ArgumentException("Inconsistent measurement report.");
        return new(b, detached);
    }
    public bool TryRun(out Task completion)
    {
        lock (gate)
        {
            if (closing || selected is null || work is { IsCompleted: false }) { completion = Task.CompletedTask; return false; }
            FrameSelection captured = selected; long identity = revision;
            report = null; error = null;
            work = Task.Run(() =>
            {
                RenderedMeasurement? output = null; string? failure = null;
                try { output = Detach(analyze(captured), captured); }
                catch (Exception ex)
                {
                    while (ex is TypeInitializationException && ex.InnerException is { } inner) ex = inner;
                    failure = ex.GetType().Name;
                }
                lock (gate)
                {
                    if (closing || revision != identity) return;
                    report = output?.Report; error = failure;
                    if (output is not null) bitmap = output.Bitmap;
                }
            });
            completion = work; return true;
        }
    }
    public Task CloseAsync()
    {
        lock (gate)
        {
            closing = true; selected = null; bitmap = null; report = null; error = null;
            return work ?? Task.CompletedTask;
        }
    }
}
