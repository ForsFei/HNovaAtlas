namespace IndustrialYolo;

public readonly record struct ImageRegion(int X, int Y, int Width, int Height);

// Owned, top-down BGR24 bytes. No SDK buffers, image codecs or native handles.
public sealed class Bgr24Image
{
    private readonly byte[] pixels;
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }

    private Bgr24Image(int width, int height, int stride, ReadOnlySpan<byte> source)
    { Width = width; Height = height; Stride = stride; pixels = source.ToArray(); }

    public static Bgr24Image Create(int width, int height, int stride, ReadOnlySpan<byte> source)
    {
        LetterboxTransform.ValidateSource(width, height);
        FixtureJson.Require(stride >= width * 3 && (long)stride * height <= 67_108_864,
            "BGR24 needs a positive stride >= width*3 and at most 64MiB.");
        FixtureJson.Require(source.Length == (long)stride * height, "Buffer must include every complete row and its padding.");
        return new(width, height, stride, source);
    }

    public byte Get(int x, int y, int bgrChannel)
    {
        FixtureJson.Require(x >= 0 && x < Width && y >= 0 && y < Height && bgrChannel is >= 0 and < 3,
            "BGR pixel index out of bounds.");
        return pixels[y * Stride + x * 3 + bgrChannel];
    }
}

public sealed record RestoredBox(Box? Original, bool WasClipped);

// An explicit teaching policy, not an adapter for every YOLO export/reference implementation.
public sealed class LetterboxTransform
{
    public int SourceWidth { get; }
    public int SourceHeight { get; }
    public ImageRegion Region { get; }
    public int InputWidth { get; }
    public int InputHeight { get; }
    public int ResizedWidth { get; }
    public int ResizedHeight { get; }
    public int PadLeft { get; }
    public int PadTop { get; }
    public int PadRight => InputWidth - ResizedWidth - PadLeft;
    public int PadBottom => InputHeight - ResizedHeight - PadTop;
    public double IdealScale { get; }
    public double ScaleX => (double)ResizedWidth / Region.Width;
    public double ScaleY => (double)ResizedHeight / Region.Height;
    public bool AllowUpscale { get; }

    private LetterboxTransform(int width, int height, ImageRegion region, int inputWidth, int inputHeight, bool allowUpscale)
    {
        SourceWidth = width; SourceHeight = height; Region = region;
        InputWidth = inputWidth; InputHeight = inputHeight; AllowUpscale = allowUpscale;
        double ratio = Math.Min((double)inputWidth / region.Width, (double)inputHeight / region.Height);
        IdealScale = allowUpscale ? ratio : Math.Min(ratio, 1);
        ResizedWidth = Math.Max(1, (int)Math.Round(region.Width * IdealScale, MidpointRounding.ToEven));
        ResizedHeight = Math.Max(1, (int)Math.Round(region.Height * IdealScale, MidpointRounding.ToEven));
        PadLeft = (inputWidth - ResizedWidth) / 2;
        PadTop = (inputHeight - ResizedHeight) / 2;
    }

    public static LetterboxTransform Create(int width, int height, ImageRegion region,
        int inputWidth, int inputHeight, bool allowUpscale = true)
    {
        ValidateSource(width, height);
        FixtureJson.Require(region.X >= 0 && region.Y >= 0 && region.Width > 0 && region.Height > 0 &&
            (long)region.X + region.Width <= width && (long)region.Y + region.Height <= height,
            "ROI must be positive and contained in the source image.");
        FixtureJson.Require(inputWidth is > 0 and <= 2048 && inputHeight is > 0 and <= 2048,
            "Teaching input sides must be 1..2048.");
        return new(width, height, region, inputWidth, inputHeight, allowUpscale);
    }

    internal static void ValidateSource(int width, int height) => FixtureJson.Require(
        width is > 0 and <= 8192 && height is > 0 and <= 8192 && (long)width * height <= 16_777_216,
        "Source dimensions must be positive, <=8192 per side and <=16777216 pixels.");

    // Input: continuous ORIGINAL-image xyxy edges, contained in this ROI.
    public Box ToInput(Box original)
    {
        BoxMath.Validate(original, SourceWidth, SourceHeight);
        FixtureJson.Require(original.X1 >= Region.X && original.Y1 >= Region.Y &&
            original.X2 <= Region.X + Region.Width && original.Y2 <= Region.Y + Region.Height,
            "Original box must be contained in this ROI.");
        return new((original.X1 - Region.X) * ScaleX + PadLeft,
            (original.Y1 - Region.Y) * ScaleY + PadTop,
            (original.X2 - Region.X) * ScaleX + PadLeft,
            (original.Y2 - Region.Y) * ScaleY + PadTop);
    }

    // Input must already be decoded to continuous INPUT-canvas xyxy, not raw/normalized output.
    public RestoredBox Restore(Box input)
    {
        FixtureJson.Require(input is not null && double.IsFinite(input.X1) && double.IsFinite(input.Y1) &&
            double.IsFinite(input.X2) && double.IsFinite(input.Y2) &&
            Math.Abs(input.X1) <= 1_000_000 && Math.Abs(input.Y1) <= 1_000_000 &&
            Math.Abs(input.X2) <= 1_000_000 && Math.Abs(input.Y2) <= 1_000_000 &&
            input.X2 > input.X1 && input.Y2 > input.Y1,
            "Decoded input box must be finite, ordered and within the +/-1e6 teaching bound.");
        double x1 = Math.Max(input.X1, PadLeft), y1 = Math.Max(input.Y1, PadTop);
        double x2 = Math.Min(input.X2, PadLeft + ResizedWidth), y2 = Math.Min(input.Y2, PadTop + ResizedHeight);
        if (x2 <= x1 || y2 <= y1) return new(null, true);
        bool clipped = x1 != input.X1 || y1 != input.Y1 || x2 != input.X2 || y2 != input.Y2;
        // Clamp only numerical roundoff after explicit content intersection; never invent a box in padding.
        Box original = new(
            Math.Clamp((x1 - PadLeft) / ScaleX + Region.X, Region.X, Region.X + Region.Width),
            Math.Clamp((y1 - PadTop) / ScaleY + Region.Y, Region.Y, Region.Y + Region.Height),
            Math.Clamp((x2 - PadLeft) / ScaleX + Region.X, Region.X, Region.X + Region.Width),
            Math.Clamp((y2 - PadTop) / ScaleY + Region.Y, Region.Y, Region.Y + Region.Height));
        BoxMath.Validate(original, SourceWidth, SourceHeight);
        return new(original, clipped);
    }
}

public sealed class PreparedInput
{
    private readonly float[] values;
    public LetterboxTransform Transform { get; }
    public byte PaddingValue { get; }
    public IReadOnlyList<long> Shape { get; }
    public ReadOnlySpan<float> Values => values;
    public float[] CopyValues() => (float[])values.Clone();

    internal PreparedInput(LetterboxTransform transform, byte paddingValue, float[] ownedValues)
    {
        Transform = transform; PaddingValue = paddingValue; values = ownedValues;
        Shape = Array.AsReadOnly(new long[] { 1, 3, transform.InputHeight, transform.InputWidth });
    }

    public float Get(int rgbChannel, int y, int x)
    {
        FixtureJson.Require(rgbChannel is >= 0 and < 3 && x >= 0 && x < Transform.InputWidth &&
            y >= 0 && y < Transform.InputHeight, "NCHW tensor index out of bounds.");
        return values[(rgbChannel * Transform.InputHeight + y) * Transform.InputWidth + x];
    }
}

public static class ImagePreprocessor
{
    // RGB float32 [1,3,H,W], /255, fixed centered canvas. No auto-stride, scale-fill or mean/std.
    public static PreparedInput Prepare(Bgr24Image image, LetterboxTransform transform, byte paddingValue = 114)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(transform);
        FixtureJson.Require(image.Width == transform.SourceWidth && image.Height == transform.SourceHeight,
            "Transform source dimensions do not match this image.");
        int plane = transform.InputWidth * transform.InputHeight;
        float[] values = new float[3 * plane];
        Array.Fill(values, paddingValue / 255f);
        for (int y = 0; y < transform.ResizedHeight; y++)
        {
            // Half-pixel centers for sampling; geometry above instead transforms area EDGES.
            double sy = Math.Clamp((y + .5) / transform.ScaleY - .5, 0, transform.Region.Height - 1);
            int y0 = (int)Math.Floor(sy), y1 = Math.Min(y0 + 1, transform.Region.Height - 1);
            double fy = sy - y0;
            for (int x = 0; x < transform.ResizedWidth; x++)
            {
                double sx = Math.Clamp((x + .5) / transform.ScaleX - .5, 0, transform.Region.Width - 1);
                int x0 = (int)Math.Floor(sx), x1 = Math.Min(x0 + 1, transform.Region.Width - 1);
                double fx = sx - x0;
                for (int rgb = 0; rgb < 3; rgb++)
                {
                    int bgr = 2 - rgb;
                    double top = (1 - fx) * Pixel(x0, y0, bgr) + fx * Pixel(x1, y0, bgr);
                    double bottom = (1 - fx) * Pixel(x0, y1, bgr) + fx * Pixel(x1, y1, bgr);
                    // Our explicit byte-quantization rule; not a claim of bit parity with OpenCV.
                    byte resized = (byte)Math.Clamp(Math.Round((1 - fy) * top + fy * bottom,
                        MidpointRounding.ToEven), 0, 255);
                    int index = rgb * plane + (y + transform.PadTop) * transform.InputWidth + x + transform.PadLeft;
                    values[index] = resized / 255f;
                }
            }
        }
        return new(transform, paddingValue, values);

        byte Pixel(int x, int y, int bgr) => image.Get(x + transform.Region.X, y + transform.Region.Y, bgr);
    }
}
