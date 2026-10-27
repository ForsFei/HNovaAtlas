using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IndustrialVision.Wpf;

public static class Mono8Bitmap
{
    public static BitmapSource Create(Mono8Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        BitmapSource bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96,
            PixelFormats.Gray8, null, image.CopyPixels(), image.Width);
        bitmap.Freeze(); return bitmap;
    }
}

public sealed class Mono8Viewer : FrameworkElement
{
    private CaptureResult? frame;
    private BitmapSource? bitmap;
    private PixelRoi? overlay;
    public Mono8Viewer()
    {
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }
    public void Present(FrameDisplayState state)
    {
        Dispatcher.VerifyAccess();
        if (!ReferenceEquals(frame, state.Current))
        {
            BitmapSource? nextBitmap = state.Current is { } next ? Mono8Bitmap.Create(next.Evidence.Frame.Image) : null;
            frame = state.Current; bitmap = nextBitmap;
        }
        overlay = state.OverlayRegion; InvalidateVisual();
    }
    private bool Viewport(out UniformImageViewport viewport)
    {
        viewport = null!;
        return frame is { } current && UniformImageViewport.TryCreate(current.Evidence.Frame.Image.Width,
            current.Evidence.Frame.Image.Height, ActualWidth, ActualHeight, out viewport);
    }
    public bool TryPick(Point location, out PixelHit hit, out byte value)
    {
        Dispatcher.VerifyAccess(); hit = default; value = default;
        if (!Viewport(out UniformImageViewport viewport) || !viewport.TryPickPixel(new(location.X, location.Y), out hit)) return false;
        value = frame!.Evidence.Frame.Image.GetPixel(hit.Column, hit.Row); return true;
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        drawing.DrawRectangle(Brushes.Black, null, new Rect(RenderSize));
        if (bitmap is null || !Viewport(out UniformImageViewport viewport)) return;
        DisplayRect bounds = viewport.ImageBounds;
        Rect imageRect = new(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        drawing.DrawImage(bitmap, imageRect);
        if (overlay is { } roi && viewport.TryRoiBounds(roi, out DisplayRect rectangle))
        {
            drawing.PushClip(new RectangleGeometry(imageRect));
            drawing.DrawRectangle(null, new Pen(Brushes.Cyan, 2), new(rectangle.Left, rectangle.Top, rectangle.Width, rectangle.Height));
            drawing.Pop();
        }
    }
}
