using System.Globalization;
using System.Windows;
using System.Windows.Media;
using IndustrialYolo;

namespace IndustrialYolo.Wpf;

public sealed record DisplayMapping(double Scale, Rect ImageArea)
{
    public static DisplayMapping? Create(int width, int height, double viewportWidth, double viewportHeight)
    {
        if (width <= 0 || height <= 0 || !double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) ||
            viewportWidth <= 0 || viewportHeight <= 0) return null;
        double scale = Math.Min(viewportWidth / width, viewportHeight / height);
        return new(scale, new((viewportWidth - width * scale) / 2, (viewportHeight - height * scale) / 2,
            width * scale, height * scale));
    }
    public Rect Map(Box box) => new(ImageArea.X + box.X1 * Scale, ImageArea.Y + box.Y1 * Scale,
        (box.X2 - box.X1) * Scale, (box.Y2 - box.Y1) * Scale);
}

public sealed class YoloViewer : FrameworkElement
{
    private ImageSnapshot? image;
    private OverlayResult? result;
    public void Present(ImageSnapshot? current, OverlayResult? overlay)
    {
        Dispatcher.VerifyAccess();
        if (overlay is not null && !ReferenceEquals(overlay.Image, current)) throw new ArgumentException("Cross-image overlay refused.");
        image = current; result = overlay; InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.Black, null, new Rect(RenderSize));
        if (image is null || DisplayMapping.Create(image.Bitmap.PixelWidth, image.Bitmap.PixelHeight,
                ActualWidth, ActualHeight) is not { } mapping) return;
        dc.PushClip(new RectangleGeometry(mapping.ImageArea));
        dc.DrawImage(image.Bitmap, mapping.ImageArea);
        if (result is not null)
        {
            Pen pen = new(Brushes.Lime, 2); pen.Freeze();
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            foreach (OverlayBox box in result.Boxes)
            {
                Rect rect = mapping.Map(box.SourceBox);
                dc.DrawRectangle(null, pen, rect);
                FormattedText text = new($"#{box.ClassId} {box.Score:F3}", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.Lime, pixelsPerDip);
                dc.DrawText(text, new Point(rect.Left + 3, rect.Top + 3));
            }
        }
        dc.Pop();
    }
}
