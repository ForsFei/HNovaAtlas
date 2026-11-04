using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IndustrialYolo;
using IndustrialYolo.Images;

namespace IndustrialYolo.Wpf;

public sealed class ImageSnapshot
{
    public Guid Id { get; } = Guid.NewGuid();
    public LoadedImage Source { get; }
    public BitmapSource Bitmap { get; }
    private ImageSnapshot(LoadedImage source, BitmapSource bitmap) { Source = source; Bitmap = bitmap; }
    public static ImageSnapshot Create(LoadedImage source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Bgr24Image image = source.Bgr;
        byte[] packed = new byte[checked(image.Width * image.Height * 3)];
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
        for (int c = 0; c < 3; c++) packed[(y * image.Width + x) * 3 + c] = image.Get(x, y, c);
        BitmapSource bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96,
            PixelFormats.Bgr24, null, packed, image.Width * 3);
        bitmap.Freeze();
        return new(source, bitmap);
    }
}

public sealed record OverlayBox(int Row, int ClassId, float Score, Box SourceBox);
public sealed class OverlayResult
{
    public ImageSnapshot Image { get; }
    public IReadOnlyList<OverlayBox> Boxes { get; }
    public int Kept { get; }
    internal OverlayResult(ImageSnapshot image, IEnumerable<OverlayBox> boxes, int kept)
    {
        OverlayBox[] copy = boxes.ToArray();
        if (kept < copy.Length || kept > 3549 || copy.Select(b => b.Row).Distinct().Count() != copy.Length)
            throw new ArgumentException("Invalid detection counts or duplicate row.");
        foreach (OverlayBox box in copy)
        {
            if (box.Row is < 0 or >= 3549 || box.ClassId is < 0 or >= 80 || !float.IsFinite(box.Score) || box.Score is < 0 or > 1)
                throw new ArgumentException("Invalid fixed-model label/score/row.");
            BoxMath.Validate(box.SourceBox, image.Source.Bgr.Width, image.Source.Bgr.Height);
        }
        Image = image; Boxes = Array.AsReadOnly(copy); Kept = kept;
    }
    public static OverlayResult FromReport(ImageSnapshot image, ImageReport report)
    {
        if (!ReferenceEquals(image.Source, report.Prepared.Source)) throw new ArgumentException("Report belongs to another loaded image object.");
        return new(image, report.Detections.Where(d => d.VisibleSource.Original is not null).Select(d =>
            new OverlayBox(d.Candidate.NetworkRow.RowIndex, d.Candidate.NetworkRow.ClassId,
                d.Candidate.NetworkRow.Score, d.VisibleSource.Original!)), report.Detections.Count);
    }
}

public sealed record InferenceRequest(Guid Session, long Number, long Selection, ImageSnapshot Image);
public enum Attachment { Attached, Stale, Failed, Rejected }

// One UI owner, one inference in flight. Clear/Show never release the running slot.
public sealed class DisplaySession
{
    private readonly Dispatcher owner = Dispatcher.CurrentDispatcher;
    private readonly Guid session = Guid.NewGuid();
    private long next, selection;
    private InferenceRequest? active;
    public ImageSnapshot? Current { get; private set; }
    public OverlayResult? Result { get; private set; }
    public string? Error { get; private set; }
    public bool Busy => active is not null;
    public bool Closing { get; private set; }
    public void Show(ImageSnapshot image)
    {
        owner.VerifyAccess();
        if (Closing) throw new InvalidOperationException("Session closing.");
        selection = checked(selection + 1);
        Current = image ?? throw new ArgumentNullException(nameof(image)); Result = null; Error = null;
    }
    public void Clear()
    { owner.VerifyAccess(); selection = checked(selection + 1); Current = null; Result = null; Error = null; }
    public InferenceRequest? TryBegin()
    {
        owner.VerifyAccess();
        if (Closing || Busy || Current is null) return null;
        Result = null; Error = null;
        return active = new(session, checked(++next), selection, Current);
    }
    public Attachment Complete(InferenceRequest request, OverlayResult? result, string? error = null)
    {
        owner.VerifyAccess();
        if (!ReferenceEquals(active, request)) return Attachment.Rejected;
        active = null;
        if (Closing || request.Selection != selection || !ReferenceEquals(Current, request.Image)) return Attachment.Stale;
        if (error is not null || result is null || !ReferenceEquals(result.Image, request.Image))
        { Result = null; Error = error ?? "Result/image identity mismatch."; return Attachment.Failed; }
        Result = result; Error = null; return Attachment.Attached;
    }
    public void BeginClose() { owner.VerifyAccess(); Closing = true; Clear(); }
}
