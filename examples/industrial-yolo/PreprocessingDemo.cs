using System.Globalization;

namespace IndustrialYolo;

internal static class PreprocessingDemo
{
    public static void Run()
    {
        Console.WriteLine("Synthetic owned BGR bytes -> teaching RGB float32 NCHW. No model/runtime/inference.");
        // Red, blue; last two bytes are row padding, not pixels.
        Bgr24Image image = Bgr24Image.Create(2, 1, 8, new byte[] { 0, 0, 255, 255, 0, 0, 77, 77 });
        LetterboxTransform transform = LetterboxTransform.Create(2, 1, new(0, 0, 2, 1), 4, 4);
        PreparedInput input = ImagePreprocessor.Prepare(image, transform);
        Console.WriteLine($"Shape=[{string.Join(',', input.Shape)}], resized={transform.ResizedWidth}x{transform.ResizedHeight}, padding L/T/R/B={transform.PadLeft}/{transform.PadTop}/{transform.PadRight}/{transform.PadBottom}");
        for (int channel = 0; channel < 3; channel++)
        {
            Console.WriteLine($"{"RGB"[channel]} plane:");
            for (int y = 0; y < 4; y++)
                Console.WriteLine(string.Join(" ", Enumerable.Range(0, 4).Select(x => input.Get(channel, y, x).ToString("0.0000", CultureInfo.InvariantCulture))));
        }
        LetterboxTransform roi = LetterboxTransform.Create(640, 480, new(100, 50, 320, 200), 640, 640);
        Box original = new(120, 70, 220, 120);
        Box network = roi.ToInput(original);
        Console.WriteLine($"ROI source xyxy={original} -> input={network} -> restored={roi.Restore(network)}");
        Console.WriteLine($"Padding-only box: {roi.Restore(new(0, 0, 20, 20))}");
        LetterboxTransform rounded = LetterboxTransform.Create(3, 2, new(0, 0, 3, 2), 5, 5);
        Console.WriteLine(FormattableString.Invariant($"3x2 -> 5x5: resized=5x3, ideal={rounded.IdealScale:F6}, actualX={rounded.ScaleX:F6}, actualY={rounded.ScaleY:F6}; own raster-edge policy, not universal YOLO coordinates."));
    }
}
