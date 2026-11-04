using IndustrialYolo;
using IndustrialYolo.Onnx;
using OpenCvSharp;

namespace IndustrialYolo.Images;

internal static class FixtureFiles
{
    public static string CreateDirectory() => Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory,
        "verification", Guid.NewGuid().ToString("N"))).FullName;

    public static string Chart(string directory)
    {
        using Mat image = new(359, 641, MatType.CV_8UC3);
        for (int y = 0; y < 359; y++)
        for (int x = 0; x < 641; x++)
            image.Set(y, x, new Vec3b((byte)(x % 256), (byte)(y % 256), (byte)((x + 2 * y) % 256)));
        return Write(directory, "色彩图641x359.png", Encode(image, ".png"));
    }

    public static string Identity(string directory)
    {
        using Mat mat = OpenCvImageInput.ToMat(YoloXInput.CreateFixture());
        return Write(directory, "r1-416x208.png", Encode(mat, ".png"));
    }

    public static byte[] Encode(Mat image, string extension)
    {
        if (!Cv2.ImEncode(extension, image, out byte[] bytes)) throw new InvalidOperationException("Fixture encode failed.");
        return bytes;
    }

    public static string Write(string directory, string name, byte[] bytes)
    {
        string path = Path.Combine(directory, name);
        using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes);
        return path;
    }
}
