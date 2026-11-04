using IndustrialYolo.Images;
using IndustrialYolo.Onnx;

namespace IndustrialYolo.Wpf;

public static class OfflineDetector
{
    // Runs only on a caller-owned worker; no UI objects except frozen BitmapSource cross threads.
    public static OverlayResult Run(string modelPath, ImageSnapshot image, float score = .1f)
    {
        PreparedImage prepared = OpenCvImageInput.Prepare(image.Source);
        using YoloXNanoSession session = new(modelPath);
        YoloXRawOutput raw = session.Run(prepared.Tensor);
        return OverlayResult.FromReport(image, ImagePostprocessor.Process(raw, prepared, new(score, .45)));
    }
}
