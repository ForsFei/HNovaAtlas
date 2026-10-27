using OpenCvSharp;

namespace IndustrialOpenCvSharp;

public sealed record MaskStage(string Name, int ForegroundPixels, IReadOnlyList<byte> Pixels);
public sealed record SegmentationReport(int Width, int Height, SegmentationRecipe Recipe,
    MaskStage Thresholded, MaskStage Opened, MaskStage Closed);

public static class SegmentationDemo
{
    public static Mat Fixture()
    {
        byte[] pixels = SegmentationFixture.Pixels();
        Mat image = new(24, 32, MatType.CV_8UC1);
        try { for (int row = 0; row < 24; row++) for (int col = 0; col < 32; col++) image.Set(row, col, pixels[row * 32 + col]); return image; }
        catch { image.Dispose(); throw; }
    }
    private static MaskStage Snapshot(string name, Mat image)
    {
        byte[] pixels = MatBasics.CopyMono8(image);
        int count = Cv2.CountNonZero(image);
        if (pixels.Any(v => v is not 0 and not 255) || pixels.Count(v => v == 255) != count)
            throw new InvalidOperationException("Binary 0/255 mask/count agreement required.");
        return new(name, count, Array.AsReadOnly(pixels));
    }
    // Synchronous borrow: caller must retain input and exclude concurrent writes/disposal.
    public static SegmentationReport Analyze(Mat image, SegmentationRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(image); ArgumentNullException.ThrowIfNull(recipe); recipe.Validate();
        if (image.Empty() || image.Dims != 2 || image.Type() != MatType.CV_8UC1 || image.Total() > 1_048_576)
            throw new ArgumentException("Bounded nonempty 2D Mono8 input required.");
        using Mat source = image.Clone(); // Isolate ROI neighborhoods from pixels outside the ROI in a parent Mat.
        using Mat filtered = new(), binary = new(), opened = new(), closed = new();
        Size size = new(recipe.FilterSize, recipe.FilterSize);
        switch (recipe.Smoothing)
        {
            case SmoothingMode.None: source.CopyTo(filtered); break;
            case SmoothingMode.Mean: Cv2.Blur(source, filtered, size, borderType: BorderTypes.Reflect101); break;
            case SmoothingMode.Gaussian: Cv2.GaussianBlur(source, filtered, size, recipe.Sigma, recipe.Sigma, BorderTypes.Reflect101); break;
            case SmoothingMode.Median: Cv2.MedianBlur(source, filtered, recipe.FilterSize); break; // API uses replicate border internally.
        }
        Cv2.Threshold(filtered, binary, recipe.Threshold, 255, ThresholdTypes.Binary);
        using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(recipe.MorphSize, recipe.MorphSize));
        if (recipe.Opening) Cv2.MorphologyEx(binary, opened, MorphTypes.Open, kernel, iterations: 1, borderType: BorderTypes.Constant, borderValue: new Scalar(0));
        else binary.CopyTo(opened);
        if (recipe.Closing) Cv2.MorphologyEx(opened, closed, MorphTypes.Close, kernel, iterations: 1, borderType: BorderTypes.Constant, borderValue: new Scalar(0));
        else opened.CopyTo(closed);
        return new(image.Cols, image.Rows, recipe, Snapshot("threshold", binary), Snapshot("opening-stage", opened), Snapshot("closing-stage", closed));
    }
    public static void Run()
    {
        using Mat image = Fixture();
        foreach (SegmentationRecipe recipe in new[] { new SegmentationRecipe(), new SegmentationRecipe(Closing: true), new SegmentationRecipe(Smoothing: SmoothingMode.Median) })
        {
            SegmentationReport report = Analyze(image, recipe);
            Console.WriteLine($"{recipe}: foreground pixels {report.Thresholded.ForegroundPixels} -> {report.Opened.ForegroundPixels} -> {report.Closed.ForegroundPixels}; no components/product decision.");
        }
    }
}
