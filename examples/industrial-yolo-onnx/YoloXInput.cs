using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace IndustrialYolo.Onnx;

// Separate from PreparedInput: THIS artifact expects BGR 0..255, not RGB /255.
public sealed class YoloXInput
{
    public const int Side = 416;
    private readonly float[] buffer;
    internal float[] Buffer => buffer;
    public int SourceHeight { get; }
    public IReadOnlyList<long> Shape { get; } = Array.AsReadOnly(new long[] { 1, 3, Side, Side });
    public ReadOnlySpan<float> Values => buffer;
    public string Sha256 => Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(buffer.AsSpan())));

    private YoloXInput(int sourceHeight, float[] values) { SourceHeight = sourceHeight; buffer = values; }

    // Deliberately narrow: r=1 only. General resize/ROI adaptation is NOT implemented here.
    public static YoloXInput PrepareNoResize(Bgr24Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width != Side || image.Height is < 1 or > Side)
            throw new ArgumentException("This r=1 adapter requires width416 and height1..416. It does not resize.");
        int plane = Side * Side;
        float[] values = new float[3 * plane];
        Array.Fill(values, 114f);
        for (int channel = 0; channel < 3; channel++)
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < Side; x++)
            values[(channel * Side + y) * Side + x] = image.Get(x, y, channel);
        return new(image.Height, values);
    }

    public static Bgr24Image CreateFixture()
    {
        const int height = 208;
        byte[] bytes = new byte[Side * height * 3];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < Side; x++)
        {
            int index = (y * Side + x) * 3;
            bytes[index] = (byte)(x % 256);
            bytes[index + 1] = (byte)(y % 256);
            bytes[index + 2] = (byte)((x + 2 * y) % 256);
        }
        return Bgr24Image.Create(Side, height, Side * 3, bytes);
    }
}
