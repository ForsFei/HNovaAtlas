namespace IndustrialVision;

public readonly record struct DisplayPoint(double X, double Y);
public readonly record struct DisplayRect(double Left, double Top, double Width, double Height);
public readonly record struct PixelHit(int Column, int Row);

// One explicit Uniform/centered layout in device-independent units, not arbitrary WPF transforms.
public sealed class UniformImageViewport
{
    public int ImageWidth { get; }
    public int ImageHeight { get; }
    public double Scale { get; }
    public DisplayRect ImageBounds { get; }
    private UniformImageViewport(int width, int height, double scale, DisplayRect bounds)
    { ImageWidth = width; ImageHeight = height; Scale = scale; ImageBounds = bounds; }
    public static bool TryCreate(int width, int height, double viewportWidth, double viewportHeight, out UniformImageViewport viewport)
    {
        viewport = null!;
        if (width <= 0 || height <= 0 || (long)width * height > Mono8Image.MaxPixels
            || !double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight)
            || viewportWidth <= 0 || viewportHeight <= 0 || viewportWidth > 1e6 || viewportHeight > 1e6) return false;
        double scale = Math.Min(viewportWidth / width, viewportHeight / height);
        double drawnWidth = width * scale, drawnHeight = height * scale;
        if (!double.IsFinite(scale) || scale <= 0 || drawnWidth <= 0 || drawnHeight <= 0) return false;
        viewport = new(width, height, scale, new((viewportWidth - drawnWidth) / 2, (viewportHeight - drawnHeight) / 2, drawnWidth, drawnHeight));
        return true;
    }
    public bool TryToDisplay(ImagePoint center, out DisplayPoint point)
    {
        point = default;
        if (!ImageCoordinates.Inside(center, ImageWidth, ImageHeight)) return false;
        point = new(ImageBounds.Left + (center.Column + .5) * Scale, ImageBounds.Top + (center.Row + .5) * Scale);
        return true;
    }
    public bool TryPickPixel(DisplayPoint point, out PixelHit hit)
    {
        hit = default;
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)
            || point.X < ImageBounds.Left || point.Y < ImageBounds.Top
            || point.X >= ImageBounds.Left + ImageBounds.Width || point.Y >= ImageBounds.Top + ImageBounds.Height) return false;
        int column = (int)Math.Floor((point.X - ImageBounds.Left) / Scale);
        int row = (int)Math.Floor((point.Y - ImageBounds.Top) / Scale);
        if ((uint)column >= (uint)ImageWidth || (uint)row >= (uint)ImageHeight) return false;
        hit = new(column, row); return true;
    }
    public bool TryRoiBounds(PixelRoi roi, out DisplayRect bounds)
    {
        bounds = default;
        if (roi.X < 0 || roi.Y < 0 || roi.Width <= 0 || roi.Height <= 0
            || (long)roi.X + roi.Width > ImageWidth || (long)roi.Y + roi.Height > ImageHeight) return false;
        bounds = new(ImageBounds.Left + roi.X * Scale, ImageBounds.Top + roi.Y * Scale, roi.Width * Scale, roi.Height * Scale);
        return true;
    }
}

// Serialized by the UI owner. Metadata consistency is NOT authentication, freshness or product quality.
public sealed class FrameDisplayState
{
    private readonly Guid captureOwner, pipelineSession;
    private readonly ProcessingRecipe recipe;
    public CaptureResult? Current { get; private set; }
    public InspectionResult? Result { get; private set; }
    public PixelRoi? OverlayRegion => Result is { Stage: ProcessingStage.Completed, Measurement: not null } ? recipe.Region : null;
    public FrameDisplayState(Guid captureOwner, Guid pipelineSession, ProcessingRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (captureOwner == Guid.Empty || pipelineSession == Guid.Empty || recipe.ProfileRevision == 0
            || string.IsNullOrWhiteSpace(recipe.Version) || recipe.Version.Length > 64)
            throw new ArgumentException("Owner, pipeline identity and explicit recipe required.");
        this.captureOwner = captureOwner; this.pipelineSession = pipelineSession; this.recipe = recipe;
    }
    public bool Show(CaptureResult capture)
    {
        if (capture?.Request is not { } request || capture.Evidence is not { } packet || packet.Frame?.Image is not { } image
            || request.Key.OwnerSession != captureOwner || request.Key.Number == 0 || string.IsNullOrWhiteSpace(request.Workpiece)
            || request.ProfileRevision != recipe.ProfileRevision || packet.ProfileRevision != recipe.ProfileRevision
            || packet.CameraEpoch == Guid.Empty || packet.Frame.Stamp.Session == Guid.Empty || packet.Frame.Stamp.Number == 0
            || packet.TriggerSequence is null || !packet.Complete || !packet.IdentityQualified
            || recipe.Region.X < 0 || recipe.Region.Y < 0 || recipe.Region.Width <= 0 || recipe.Region.Height <= 0
            || (long)recipe.Region.X + recipe.Region.Width > image.Width || (long)recipe.Region.Y + recipe.Region.Height > image.Height) return false;
        Current = capture; Result = null; return true; // Every explicit image replacement clears all old graphics/results.
    }
    public bool TryAttach(InspectionResult result)
    {
        if (Current is not { } capture || result?.Request is null || result.PipelineSession != pipelineSession
            || result.Request != capture.Request || result.Frame != capture.Evidence.Frame.Stamp
            || result.CameraEpoch != capture.Evidence.CameraEpoch || result.TriggerSequence != capture.Evidence.TriggerSequence
            || result.Recipe != recipe) return false;
        if (result.Stage == ProcessingStage.Completed)
        {
            if (result.ErrorCode is not null || result.Measurement is not { } statistics || statistics.Region != recipe.Region
                || statistics.Count != recipe.Region.Width * recipe.Region.Height || !double.IsFinite(statistics.Mean)) return false;
        }
        else if (result.Stage != ProcessingStage.Failed || result.Measurement is not null || string.IsNullOrWhiteSpace(result.ErrorCode)) return false;
        if (Result is not null) return ReferenceEquals(Result, result); // A second conflicting terminal cannot overwrite the first.
        Result = result; return true;
    }
    public void Clear() { Current = null; Result = null; }
}
