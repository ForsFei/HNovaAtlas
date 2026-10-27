using HalconDotNet;
using IndustrialHalcon;
using IndustrialVision;

namespace IndustrialHalconWpf;

public static class NativeFrameRunner
{
    public static BlobReport Analyze(Mono8Image frame)
    {
        if (frame.Width != 8 || frame.Height != 6) throw new ArgumentException("Fixed fixture required.");
        byte[] pixels = frame.CopyPixels();
        using HTuple rows = new(Enumerable.Range(0, 48).Select(i => i / 8).ToArray());
        using HTuple columns = new(Enumerable.Range(0, 48).Select(i => i % 8).ToArray());
        using HTuple gray = new(pixels.Select(v => (int)v).ToArray());
        HOperatorSet.GenImageConst(out HObject image, "byte", 8, 6);
        using (image)
        {
            HOperatorSet.SetGrayval(image, rows, columns, gray); // New exclusively owned image only.
            return EngineDemo.AnalyzeImage(image, 200);
        }
    }
}
