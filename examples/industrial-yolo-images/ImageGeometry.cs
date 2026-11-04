using IndustrialYolo;

namespace IndustrialYolo.Images;

public sealed record VisibleImageBox(Box? Original, bool WasClipped);

public sealed class ImageGeometry
{
    public int Width { get; }
    public int Height { get; }
    public double Ratio { get; }
    public int ResizedWidth { get; }
    public int ResizedHeight { get; }
    public int PadRight => 416 - ResizedWidth;
    public int PadBottom => 416 - ResizedHeight;
    public double RasterScaleX => (double)ResizedWidth / Width;
    public double RasterScaleY => (double)ResizedHeight / Height;

    private ImageGeometry(int width, int height)
    {
        Width = width; Height = height;
        Ratio = Math.Min(416d / width, 416d / height);
        ResizedWidth = (int)(width * Ratio);
        ResizedHeight = (int)(height * Ratio);
        if (ResizedWidth < 1 || ResizedHeight < 1)
            throw new ArgumentException("Upstream int-truncated resize would have a zero side; this adapter rejects it.");
    }

    public static ImageGeometry Create(int width, int height)
    {
        ValidateDimensions(width, height);
        return new(width, height);
    }

    public static void ValidateDimensions(int width, int height)
    {
        if (width is < 1 or > 8192 || height is < 1 or > 8192 || (long)width * height > 16_777_216)
            throw new ArgumentException("Decoded image must be1..8192 per side and <=16777216 pixels.");
    }

    public Box ToNetwork(Box source)
    {
        BoxMath.Validate(source, Width, Height);
        return new(source.X1 * Ratio, source.Y1 * Ratio, source.X2 * Ratio, source.Y2 * Ratio);
    }

    // Reference-space coordinates (not clipped) for upstream-style source-space NMS.
    public Box ToReferenceSource(Box network)
    {
        ArgumentNullException.ThrowIfNull(network);
        Box source = new(network.X1 / Ratio, network.Y1 / Ratio, network.X2 / Ratio, network.Y2 / Ratio);
        // Uses the existing teaching NMS geometry bounds, never silent clipping of outliers.
        _ = IndustrialYolo.Onnx.YoloXPostprocessor.UpstreamInclusiveNmsIou(source, source);
        return source;
    }

    public VisibleImageBox ToVisibleSource(Box network)
    {
        Box reference = ToReferenceSource(network);
        double x1 = Math.Max(0, network.X1), y1 = Math.Max(0, network.Y1);
        double x2 = Math.Min(ResizedWidth, network.X2), y2 = Math.Min(ResizedHeight, network.Y2);
        if (x2 <= x1 || y2 <= y1) return new(null, true);
        Box source = new(Math.Clamp(x1 / Ratio, 0, Width), Math.Clamp(y1 / Ratio, 0, Height),
            Math.Clamp(x2 / Ratio, 0, Width), Math.Clamp(y2 / Ratio, 0, Height));
        BoxMath.Validate(source, Width, Height);
        return new(source, source != reference);
    }
}
