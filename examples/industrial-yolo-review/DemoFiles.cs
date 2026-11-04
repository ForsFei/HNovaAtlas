using System.Text;
using System.Text.Json;
using IndustrialYolo;
using IndustrialYolo.Images;
using IndustrialYolo.Onnx;
using OpenCvSharp;

namespace IndustrialYolo.Review;

internal static class DemoFiles
{
    public static string DirectoryForRun() => Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory,
        "verification", Guid.NewGuid().ToString("N"))).FullName;
    public static string Create(string directory)
    {
        byte[] pixels = new byte[641 * 359 * 3];
        for (int y = 0; y < 359; y++) for (int x = 0; x < 641; x++)
        { int i = (y * 641 + x) * 3; pixels[i] = (byte)(x % 256); pixels[i + 1] = (byte)(y % 256); pixels[i + 2] = (byte)((x + 2 * y) % 256); }
        using Mat gradient = OpenCvImageInput.ToMat(Bgr24Image.Create(641, 359, 1923, pixels));
        using Mat black = new(360, 640, MatType.CV_8UC3, new Scalar(0, 0, 0));
        if (!Cv2.ImEncode(".png", gradient, out byte[] image1) || !Cv2.ImEncode(".png", black, out byte[] image2))
            throw new InvalidOperationException("Demo encode failed.");
        byte[] label1 = Encoding.UTF8.GetBytes("0 0.5 0.5 0.2 0.2\n"), label2 = [];
        Write(Path.Combine(directory, "色彩图.png"), image1); Write(Path.Combine(directory, "negative.png"), image2);
        Write(Path.Combine(directory, "one.txt"), label1); Write(Path.Combine(directory, "negative.txt"), label2);
        ReviewManifest manifest = new(ReviewData.Schema, "owned-demo-v1", "arbitrary-truth-v1", "synthetic-check", "validation",
            ReviewData.LabelSet, ModelArtifact.Sha256, ReviewData.Pipeline, .01f, .45, .5,
            [new("gradient", "synthetic-a", "色彩图.png", "one.txt", 641, 359, ReviewData.Sha(image1), ReviewData.Sha(label1)),
             new("negative", "synthetic-b", "negative.png", "negative.txt", 640, 360, ReviewData.Sha(image2), ReviewData.Sha(label2))]);
        string path = Path.Combine(directory, "manifest.json"); Write(path, JsonSerializer.SerializeToUtf8Bytes(manifest, ReviewData.Json)); return path;
    }
    internal static void Write(string path, byte[] bytes)
    { using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); file.Write(bytes); }
}
