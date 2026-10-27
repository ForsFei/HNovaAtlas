namespace IndustrialOpenCvSharp;

// Teaching envelope for positive-stride, packed-per-pixel Mono8 input; not every OpenCV image.
public static class PixelLayout
{
    public static int Validate(int width, int height, long stride, long availableBytes)
    {
        long pixels = (long)width * height;
        if (width <= 0 || height <= 0 || pixels > 1_048_576 || stride < width || stride > 4_194_304 || availableBytes < 0)
            throw new ArgumentException("Bounded positive dimensions and positive Mono8 stride required.");
        long required = (height - 1) * stride + width;
        if (required > availableBytes) throw new ArgumentException("Last row is incomplete.");
        return (int)pixels;
    }
    public static byte[] Pack(int width, int height, int stride, byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        byte[] packed = new byte[Validate(width, height, stride, source.LongLength)];
        for (int row = 0; row < height; row++) source.AsSpan(row * stride, width).CopyTo(packed.AsSpan(row * width, width));
        return packed;
    }
    public static void ValidateRoi(int width, int height, int x, int y, int roiWidth, int roiHeight)
    {
        if (width <= 0 || height <= 0 || x < 0 || y < 0 || roiWidth <= 0 || roiHeight <= 0 ||
            (long)x + roiWidth > width || (long)y + roiHeight > height) throw new ArgumentException("Nonempty fully contained ROI required.");
    }
    public static void Checks()
    {
        int count = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); Console.WriteLine($"PASS {++count}: {label}"); }
        void Reject(Action action, string label) { try { action(); } catch (ArgumentException) { Check(true, label); return; } throw new InvalidOperationException(label); }
        Check(Validate(3, 2, 4, 7) == 6, "Last row does not require trailing padding");
        Check(Validate(8, 6, 8, 48) == 48, "Packed size is width times height for Mono8");
        byte[] source = [1, 2, 3, 99, 4, 5, 6]; byte[] packed = Pack(3, 2, 4, source);
        Check(packed.SequenceEqual(new byte[] { 1, 2, 3, 4, 5, 6 }), "Rows copy without padding");
        source[0] = 100; Check(packed[0] == 1, "Packed result owns an independent copy");
        Check(Pack(1, 1, 8, [7]).Single() == 7, "Single row needs only actual pixels");
        Reject(() => Validate(0, 2, 4, 8), "Zero width refused");
        Reject(() => Validate(3, -1, 4, 8), "Negative height refused");
        Reject(() => Validate(3, 2, 2, 8), "Short stride refused");
        Reject(() => Validate(3, 2, -4, 8), "Negative stride intentionally unsupported");
        Reject(() => Validate(3, 2, 4, 6), "Truncated last row refused");
        Reject(() => Validate(3, 2, 4, -1), "Negative buffer length refused");
        Reject(() => Validate(2048, 2048, 2048, 4_194_304), "Teaching pixel cap enforced");
        Reject(() => Validate(3, 2, long.MaxValue, long.MaxValue), "Stride cap prevents offset overflow");
        Reject(() => Pack(1, 1, 1, null!), "Missing buffer refused");
        ValidateRoi(8, 6, 1, 1, 2, 2); Check(true, "Rect x/y/width/height accepted");
        ValidateRoi(8, 6, 7, 5, 1, 1); Check(true, "Last pixel ROI is valid");
        Reject(() => ValidateRoi(8, 6, -1, 1, 2, 2), "Negative ROI origin refused");
        Reject(() => ValidateRoi(8, 6, 1, 1, 0, 2), "Empty ROI refused by teaching policy");
        Reject(() => ValidateRoi(8, 6, 7, 5, 2, 1), "Overhanging ROI refused without clamping");
        Reject(() => ValidateRoi(8, 6, int.MaxValue, 1, int.MaxValue, 2), "ROI sum checked without int overflow");
        Console.WriteLine($"Managed layout checks passed: {count}. No OpenCV native calls.");
    }
}
