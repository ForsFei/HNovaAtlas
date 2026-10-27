namespace IndustrialOpenCvSharp;

public enum SmoothingMode { None, Mean, Gaussian, Median }
public sealed record SegmentationRecipe(SmoothingMode Smoothing = SmoothingMode.None, int FilterSize = 3,
    double Sigma = 1, int Threshold = 200, int MorphSize = 3, bool Opening = true, bool Closing = false)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Smoothing) || FilterSize is < 3 or > 9 || FilterSize % 2 == 0 ||
            !double.IsFinite(Sigma) || Sigma is < .1 or > 10 || Threshold is < 0 or > 255 ||
            MorphSize is < 1 or > 9 || MorphSize % 2 == 0)
            throw new ArgumentException("Teaching bounds: known filter, odd filter 3..9, sigma .1..10, byte threshold, odd morphology 1..9.");
    }
}
public static class SegmentationFixture
{
    public static byte[] Pixels()
    {
        byte[] pixels = Enumerable.Repeat((byte)16, 32 * 24).ToArray();
        for (int row = 6; row <= 17; row++) for (int col = 6; col <= 21; col++) pixels[row * 32 + col] = 220;
        pixels[11 * 32 + 13] = 16; // One real dark pixel, not silently treated as noise.
        pixels[2 * 32 + 2] = 255;
        return pixels;
    }
    public static void Checks()
    {
        int count = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); Console.WriteLine($"PASS {++count}: {label}"); }
        void Reject(SegmentationRecipe recipe, string label) { try { recipe.Validate(); } catch (ArgumentException) { Check(true, label); return; } throw new InvalidOperationException(label); }
        SegmentationRecipe recipe = new(); recipe.Validate(); Check(!recipe.Closing && recipe.Smoothing == SmoothingMode.None, "Default preserves hole evidence, no implicit smoothing or closing");
        foreach (SmoothingMode mode in Enum.GetValues<SmoothingMode>()) { (recipe with { Smoothing = mode }).Validate(); Check(true, $"Known filter {mode}"); }
        Reject(recipe with { Smoothing = (SmoothingMode)99 }, "Unknown filter refused");
        Reject(recipe with { FilterSize = 2 }, "Small filter refused"); Reject(recipe with { FilterSize = 4 }, "Even filter refused");
        Reject(recipe with { FilterSize = 11 }, "Filter resource cap enforced");
        Reject(recipe with { Sigma = double.NaN }, "Nonfinite sigma refused"); Reject(recipe with { Sigma = 0 }, "Nonpositive sigma refused");
        Reject(recipe with { Sigma = 11 }, "Sigma teaching cap enforced");
        Reject(recipe with { Threshold = -1 }, "Negative byte threshold refused"); Reject(recipe with { Threshold = 256 }, "Byte threshold upper bound enforced");
        Reject(recipe with { MorphSize = 0 }, "Empty morphology kernel refused"); Reject(recipe with { MorphSize = 2 }, "Even morphology kernel refused");
        Reject(recipe with { MorphSize = 11 }, "Morphology teaching cap enforced");
        (recipe with { MorphSize = 1, Threshold = 255 }).Validate(); Check(true, "Identity morphology kernel and upper threshold accepted");
        byte[] pixels = Pixels(); Check(pixels.Length == 768 && pixels.Count(v => v > 200) == 192, "Original 192-pixel rectangle minus hole plus singleton");
        Check(pixels[11 * 32 + 13] == 16 && pixels[2 * 32 + 2] == 255, "Hole and isolated bright evidence remain in source");
        pixels[0] = 0; Check(Pixels()[0] == 16, "Each fixture call returns fresh owned pixels");
        Console.WriteLine($"Segmentation recipe/fixture checks passed: {count}. Managed only; no filtering or morphology.");
    }
}
