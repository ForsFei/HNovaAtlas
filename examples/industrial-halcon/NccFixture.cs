namespace IndustrialHalcon;

// Original deterministic pixels only. No NCC search, image filtering or fallback algorithm.
public static class NccFixture
{
    public const int Width = 64, Height = 64;
    public static PixelRoi TemplateRoi => new(12, 10, 35, 41); // 24x32, center=(23.5,25.5).
    public static int[] Pixels(int rowShift, int columnShift)
    {
        PixelRoi r = TemplateRoi;
        if ((long)r.Row1 + rowShift < 0 || (long)r.Row2 + rowShift >= Height ||
            (long)r.Column1 + columnShift < 0 || (long)r.Column2 + columnShift >= Width)
            throw new ArgumentOutOfRangeException(nameof(rowShift), "Translated patch must fit the fixed image.");
        int[] pixels = Enumerable.Repeat(16, Width * Height).ToArray();
        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 32; x++)
            {
                int gray = 32 + (y * 73 + x * 151 + y * x * 17 + (y ^ x) * 29) % 192;
                pixels[(r.Row1 + rowShift + y) * Width + r.Column1 + columnShift + x] = gray;
            }
        return pixels;
    }
}
