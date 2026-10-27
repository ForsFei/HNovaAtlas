using OpenCvSharp;

namespace IndustrialOpenCvSharp;

public static class StripeDemo
{
    public static StripeRecipe DefaultRecipe() => new(new(16, 16, 96, 32));
    public static Mat Fixture(bool dark = false, bool doubleStripe = false, bool horizontal = false)
    {
        Mat image = new(128, 128, MatType.CV_8UC1, new Scalar(dark ? 220 : 20));
        Scalar fill = new(dark ? 20 : 220);
        Rect box = horizontal ? new(0, 40, 128, 32) : new(40, 0, 32, 128);
        Cv2.Rectangle(image, box, fill, -1);
        if (doubleStripe) Cv2.Rectangle(image, horizontal ? new(0, 88, 128, 8) : new(88, 0, 8, 128), fill, -1);
        return image;
    }
    public static StripeReport Measure(Mat gray, StripeRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(gray);
        if (gray.Empty() || gray.Dims != 2 || gray.Type() != MatType.CV_8UC1)
            throw new ArgumentException("Borrowed nonempty 2D CV_8UC1 required.");
        int width = gray.Cols, height = gray.Rows;
        recipe.ValidateImage(width, height);
        ScanWindow w = recipe.Window;
        using Mat view = new(gray, new Rect(w.X, w.Y, w.Width, w.Height));
        using Mat isolated = view.Clone(); using Mat mean = new(); using Mat smooth = new();
        bool x = recipe.Axis == ScanAxis.X;
        Cv2.Reduce(isolated, mean, x ? ReduceDimension.Row : ReduceDimension.Column, ReduceTypes.Avg, MatType.CV_64F);
        Cv2.GaussianBlur(mean, smooth, x ? new Size(5, 1) : new Size(1, 5), recipe.Sigma, recipe.Sigma, BorderTypes.Reflect101);
        if (mean.Type() != MatType.CV_64FC1 || smooth.Type() != MatType.CV_64FC1 ||
            mean.Total() != recipe.Length || smooth.Total() != recipe.Length ||
            (x ? mean.Rows != 1 || smooth.Rows != 1 : mean.Cols != 1 || smooth.Cols != 1))
            throw new InvalidOperationException("Unexpected native projection layout.");
        double[] raw = new double[recipe.Length], filtered = new double[recipe.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            raw[i] = mean.At<double>(x ? 0 : i, x ? i : 0);
            filtered[i] = smooth.At<double>(x ? 0 : i, x ? i : 0);
        }
        return StripeAnalysis.Analyze(raw, filtered, recipe);
    }
    public static void Run()
    {
        using Mat bright = Fixture(); using Mat dark = Fixture(dark: true); using Mat many = Fixture(doubleStripe: true);
        foreach ((string name, Mat image, StripeRecipe recipe) in new[] {
            ("bright", bright, DefaultRecipe()), ("reverse bright", bright, DefaultRecipe() with { Reverse = true }),
            ("dark", dark, DefaultRecipe() with { Polarity = StripePolarity.Dark }), ("double stripe", many, DefaultRecipe()) })
        {
            StripeReport report = Measure(image, recipe);
            Console.WriteLine($"{name}: {report.Outcome}; edges={report.Edges.Count}, pairs={report.Pairs.Count}, width={report.WidthPixels?.ToString("F6") ?? "absent"}px");
            foreach (ScanEdge edge in report.Edges) Console.WriteLine($"  scan={edge.Position:F6}, signed peak={edge.SignedPeak:F6}");
            Console.WriteLine($"  image points: {report.FirstImagePoint} -> {report.SecondImagePoint}");
        }
        Console.WriteLine("Axis-aligned projected profile only; no tilt verification, subpixel accuracy claim, calibration, product decision or device command.");
    }
}
