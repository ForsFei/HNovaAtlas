namespace IndustrialVision;

public enum PixelEncoding { Unknown, Mono8, Mono12Packed, BayerRG8 }
public readonly record struct PixelRoi(int X, int Y, int Width, int Height)
{
    public void Validate(int imageWidth, int imageHeight)
    {
        if (X < 0 || Y < 0 || Width <= 0 || Height <= 0
            || (long)X + Width > imageWidth || (long)Y + Height > imageHeight)
            throw new ArgumentException("ROI must be nonempty and fully inside the image.");
    }
}
public sealed record PixelStatistics(PixelRoi Region, int Count, byte Min, byte Max,
    double Mean, int AtOrAboveThreshold, int BlackPixels, int WhitePixels);
public sealed record ImageCrop(Mono8Image Image, PixelRoi SourceRegion);

// Managed, owned, tightly packed Mono8 pixels; no IDisposable needed.
public sealed class Mono8Image
{
    public const int MaxPixels = 1_048_576; // Teaching resource cap, not a camera limit.
    private readonly byte[] pixels;
    public int Width { get; }
    public int Height { get; }
    private Mono8Image(int width, int height, byte[] owned) { Width = width; Height = height; pixels = owned; }
    public static int PixelCount(int width, int height)
    {
        long size = (long)width * height;
        if (width <= 0 || height <= 0 || size > MaxPixels) throw new ArgumentException("Invalid dimensions or teaching pixel cap exceeded.");
        return (int)size;
    }
    public static Mono8Image FromPacked(int width, int height, ReadOnlySpan<byte> source)
    {
        int count = PixelCount(width, height);
        if (source.Length != count) throw new ArgumentException("Packed Mono8 byte count must exactly match dimensions.");
        return new(width, height, source.ToArray());
    }
    public static Mono8Image CopyFromBuffer(int width, int height, int stride, ReadOnlySpan<byte> source,
        PixelEncoding encoding, bool complete = false)
    {
        int count = PixelCount(width, height);
        if (!complete || encoding != PixelEncoding.Mono8 || stride < width)
            throw new ArgumentException("Only externally confirmed complete, positive-stride Mono8 supported.");
        long minimum = (long)(height - 1) * stride + width;
        if (minimum > source.Length) throw new ArgumentException("Truncated rows.");
        byte[] owned = new byte[count];
        for (int y = 0; y < height; y++) source.Slice(y * stride, width).CopyTo(owned.AsSpan(y * width, width));
        return new(width, height, owned);
    }
    public byte GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(x));
        return pixels[y * Width + x];
    }
    public byte[] CopyPixels() => (byte[])pixels.Clone();
    public ImageCrop Crop(PixelRoi roi)
    {
        roi.Validate(Width, Height);
        byte[] owned = new byte[PixelCount(roi.Width, roi.Height)];
        for (int y = 0; y < roi.Height; y++)
            pixels.AsSpan((y + roi.Y) * Width + roi.X, roi.Width).CopyTo(owned.AsSpan(y * roi.Width, roi.Width));
        return new(new(roi.Width, roi.Height, owned), roi);
    }
    public PixelStatistics Measure(PixelRoi roi, byte threshold)
    {
        roi.Validate(Width, Height);
        int count = roi.Width * roi.Height, bright = 0, black = 0, white = 0;
        byte min = 255, max = 0; long sum = 0;
        for (int y = roi.Y; y < roi.Y + roi.Height; y++)
            for (int x = roi.X; x < roi.X + roi.Width; x++)
            {
                byte value = pixels[y * Width + x];
                min = Math.Min(min, value); max = Math.Max(max, value); sum += value;
                if (value >= threshold) bright++;
                if (value == 0) black++;
                if (value == 255) white++;
            }
        return new(roi, count, min, max, (double)sum / count, bright, black, white);
    }
}
