using OpenCvSharp;

namespace IndustrialOpenCvSharp;

public static class SegmentationChecks
{
    public static void Run()
    {
        int count = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); Console.WriteLine($"PASS {++count}: {label}"); }
        void Reject(Action action, string label) { try { action(); } catch (ArgumentException) { Check(true, label); return; } throw new InvalidOperationException(label); }
        using Mat image = SegmentationDemo.Fixture(); byte[] original = MatBasics.CopyMono8(image);
        SegmentationReport result = SegmentationDemo.Analyze(image, new());
        Check(result.Thresholded.ForegroundPixels == 192 && result.Opened.ForegroundPixels == 191, "Actual opening removes singleton while retaining dark hole");
        Check(result.Closed.ForegroundPixels == 191 && !result.Recipe.Closing, "Disabled closing is a copied stage, not implicit fill");
        Check(result.Thresholded.Pixels[2 * 32 + 2] == 255 && result.Opened.Pixels[2 * 32 + 2] == 0, "Isolated evidence loss visible in separate snapshots");
        Check(result.Opened.Pixels[11 * 32 + 13] == 0, "Opening is not hole filling");
        SegmentationReport filled = SegmentationDemo.Analyze(image, new(Closing: true));
        Check(filled.Closed.ForegroundPixels == 192 && filled.Closed.Pixels[11 * 32 + 13] == 255, "Optional closing actually erases dark-hole evidence");
        Check(!filled.Closed.Pixels.SequenceEqual(filled.Thresholded.Pixels), "Same foreground count can hide different masks");
        Check(MatBasics.CopyMono8(image).SequenceEqual(original), "Pipeline does not modify borrowed input");
        try { ((IList<byte>)result.Opened.Pixels)[0] = 255; throw new InvalidOperationException("Read-only expected"); }
        catch (NotSupportedException) { Check(true, "Published mask snapshots are read-only"); }
        using Mat black = new(24, 32, MatType.CV_8UC1, new Scalar(16));
        Check(SegmentationDemo.Analyze(black, new()).Closed.ForegroundPixels == 0, "Uniform dark scene is a real empty mask, not an exception");
        Check(SegmentationDemo.Analyze(image, new(Threshold: 255)).Thresholded.ForegroundPixels == 0, "Binary threshold255 has no greater byte values");
        Check(SegmentationDemo.Analyze(image, new(MorphSize: 1, Closing: true)).Closed.Pixels.SequenceEqual(result.Thresholded.Pixels), "One-by-one morphology is identity");
        foreach (SmoothingMode mode in Enum.GetValues<SmoothingMode>())
        {
            SegmentationReport report = SegmentationDemo.Analyze(image, new(Smoothing: mode));
            Check(report.Width == 32 && report.Height == 24 && report.Thresholded.Pixels.Count == 768, $"Real filter path {mode} preserves dimensions and binary snapshot");
        }
        using Mat empty = new(), color = new(2, 2, MatType.CV_8UC3, new Scalar(0));
        Reject(() => SegmentationDemo.Analyze(empty, new()), "Empty native input refused");
        Reject(() => SegmentationDemo.Analyze(color, new()), "Color input refused, no implicit grayscale conversion");
        using Mat edge = new(1, 3, MatType.CV_8UC1); edge.Set(0, 0, (byte)199); edge.Set(0, 1, (byte)200); edge.Set(0, 2, (byte)201);
        using Mat binary = new(), inverse = new(), inclusive = new();
        double used = Cv2.Threshold(edge, binary, 200, 255, ThresholdTypes.Binary);
        Cv2.Threshold(edge, inverse, 200, 255, ThresholdTypes.BinaryInv);
        Cv2.InRange(edge, new Scalar(200), new Scalar(255), inclusive);
        Check(used == 200 && MatBasics.CopyMono8(binary).SequenceEqual(new byte[] { 0, 0, 255 }), "Binary uses strict greater-than, returned double is threshold");
        Check(MatBasics.CopyMono8(inverse).SequenceEqual(new byte[] { 255, 255, 0 }), "BinaryInv includes equality in white output");
        Check(MatBasics.CopyMono8(inclusive).SequenceEqual(new byte[] { 0, 255, 255 }), "InRange includes both endpoints, unlike Binary equality");
        using Mat impulse = new(9, 9, MatType.CV_8UC1, new Scalar(0)); impulse.Set(4, 4, (byte)255);
        using Mat mean = new(), gaussian = new(), median = new();
        Cv2.Blur(impulse, mean, new Size(3, 3), borderType: BorderTypes.Reflect101);
        Cv2.GaussianBlur(impulse, gaussian, new Size(3, 3), 1, 1, BorderTypes.Reflect101);
        Cv2.MedianBlur(impulse, median, 3);
        Check(mean.At<byte>(4, 4) == 28, "Mean spreads impulse into neighborhood");
        Check(gaussian.At<byte>(4, 4) is > 28 and < 255, "Gaussian weights center but still changes evidence");
        Check(Cv2.CountNonZero(median) == 0, "Median removes isolated bright pixel entirely");
        using Mat uniform = new(9, 9, MatType.CV_8UC1, new Scalar(100)), adaptive = new(), adaptiveZero = new();
        Cv2.AdaptiveThreshold(uniform, adaptive, 255, AdaptiveThresholdTypes.MeanC, ThresholdTypes.Binary, 3, 2);
        Cv2.AdaptiveThreshold(uniform, adaptiveZero, 255, AdaptiveThresholdTypes.MeanC, ThresholdTypes.Binary, 3, 0);
        Check(Cv2.CountNonZero(adaptive) == 81 && Cv2.CountNonZero(adaptiveZero) == 0, "Adaptive mean-minus-C can label uniform background white");
        using Mat bimodal = new(2, 4, MatType.CV_8UC1);
        for (int row = 0; row < 2; row++) for (int col = 0; col < 4; col++) bimodal.Set(row, col, (byte)(col < 2 ? 10 : 220));
        using Mat otsu = new(); double threshold = Cv2.Threshold(bimodal, otsu, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        Check(threshold == 10 && Cv2.CountNonZero(otsu) == 4, "Actual two-level Otsu chooses10, not product confidence");
        using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)), eroded = new(), dilated = new();
        using Mat square = new(7, 7, MatType.CV_8UC1, new Scalar(0));
        for (int row = 2; row <= 4; row++) for (int col = 2; col <= 4; col++) square.Set(row, col, (byte)255);
        Cv2.Erode(square, eroded, kernel, iterations: 1, borderType: BorderTypes.Constant, borderValue: new Scalar(0));
        Cv2.Dilate(square, dilated, kernel, iterations: 1, borderType: BorderTypes.Constant, borderValue: new Scalar(0));
        Check(Cv2.CountNonZero(eroded) == 1 && Cv2.CountNonZero(dilated) == 25, "3x3 square erodes to1 or dilates to25 pixels");
        using Mat full = new(3, 3, MatType.CV_8UC1, new Scalar(255)), zeroBorder = new(), replicateBorder = new();
        Cv2.Erode(full, zeroBorder, kernel, borderType: BorderTypes.Constant, borderValue: new Scalar(0));
        Cv2.Erode(full, replicateBorder, kernel, borderType: BorderTypes.Replicate);
        Check(Cv2.CountNonZero(zeroBorder) == 1 && Cv2.CountNonZero(replicateBorder) == 9, "Explicit border policy changes edge-touching foreground");
        using Mat parent = new(5, 5, MatType.CV_8UC1, new Scalar(0)); parent.Set(2, 1, (byte)255);
        using Mat view = new(parent, new Rect(2, 1, 2, 3)), isolated = view.Clone(), fromView = new(), fromClone = new();
        Cv2.Blur(view, fromView, new Size(3, 3), borderType: BorderTypes.Reflect101);
        Cv2.Blur(isolated, fromClone, new Size(3, 3), borderType: BorderTypes.Reflect101);
        Check(Cv2.CountNonZero(fromView) > 0 && Cv2.CountNonZero(fromClone) == 0 &&
            SegmentationDemo.Analyze(view, new(Smoothing: SmoothingMode.Mean, Threshold: 0, Opening: false)).Thresholded.ForegroundPixels == 0,
            "Pipeline clone isolates ROI from real parent-neighbor influence");
        Console.WriteLine($"Native segmentation checks passed: {count}. Actual filters/thresholds/morphology; no component count, calibration, product decision or long-term performance validation.");
    }
}
