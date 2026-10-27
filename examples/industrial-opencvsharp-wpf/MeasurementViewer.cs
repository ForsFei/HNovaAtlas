using System.Windows;
using System.Windows.Media;
using IndustrialOpenCvSharp;

namespace IndustrialOpenCvSharpWpf;

public sealed record ImageViewport(double Scale, Rect Bounds)
{
    public static ImageViewport? Create(int width, int height, double areaWidth, double areaHeight)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 1048576 ||
            !double.IsFinite(areaWidth) || !double.IsFinite(areaHeight) || areaWidth <= 0 || areaHeight <= 0 || areaWidth > 1e6 || areaHeight > 1e6) return null;
        double scale = Math.Min(areaWidth / width, areaHeight / height);
        if (!double.IsFinite(scale) || scale <= 0) return null;
        return new(scale, new((areaWidth - width * scale) / 2, (areaHeight - height * scale) / 2, width * scale, height * scale));
    }
    public Point PixelCenter(SamplePoint p) => new(Bounds.Left + (p.X + .5) * Scale, Bounds.Top + (p.Y + .5) * Scale);
    public Rect Window(ScanWindow w) => new(Bounds.Left + w.X * Scale, Bounds.Top + w.Y * Scale, w.Width * Scale, w.Height * Scale);
}

public sealed class MeasurementViewer : FrameworkElement
{
    private FrameView? current;
    public MeasurementViewer()
    {
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }
    public void Present(FrameView view)
    {
        Dispatcher.VerifyAccess(); current = view; InvalidateVisual();
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        drawing.DrawRectangle(Brushes.Black, null, new Rect(RenderSize));
        if (current is not { Selected: { } frame, Bitmap: { } bitmap } ||
            ImageViewport.Create(frame.Image.Width, frame.Image.Height, ActualWidth, ActualHeight) is not { } viewport) return;
        drawing.DrawImage(bitmap, viewport.Bounds);
        drawing.PushClip(new RectangleGeometry(viewport.Bounds));
        drawing.DrawRectangle(null, new Pen(Brushes.Cyan, 2), viewport.Window(frame.Recipe.Window));
        if (current.Report is { Outcome: StripeOutcome.WidthMeasured, FirstImagePoint: { } a, SecondImagePoint: { } b })
        {
            Point start = viewport.PixelCenter(a), end = viewport.PixelCenter(b);
            drawing.DrawLine(new Pen(Brushes.Orange, 3), start, end);
            drawing.DrawEllipse(Brushes.Orange, null, start, 3, 3);
            drawing.DrawEllipse(Brushes.Orange, null, end, 3, 3);
        }
        drawing.Pop();
    }
}
