namespace IndustrialHalcon;

public static class SegmentationChecks
{
    public static void Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            Console.WriteLine($"PASS {++count}: {label}");
        }
        void Reject(Action action, string label)
        {
            try { action(); } catch (ArgumentException) { Check(true, label); return; }
            throw new InvalidOperationException($"Expected invalid parameter rejection: {label}");
        }
        SegmentationRecipe recipe = SegmentationRecipe.Default;
        recipe.Validate(16, 16);
        Check(recipe.Roi.Area == 144, "Inclusive 2..13 rectangle has 12x12 pixel centers");
        new PixelRoi(0, 0, 15, 15).Validate(16, 16);
        Check(new PixelRoi(0, 0, 15, 15).Area == 256, "Last center is 15, not width 16");
        new PixelRoi(5, 7, 5, 7).Validate(16, 16);
        Check(new PixelRoi(5, 7, 5, 7).Area == 1, "One-pixel inclusive ROI is geometrically valid");
        Reject(() => new PixelRoi(-1, 2, 13, 13).Validate(16, 16), "Negative row refused");
        Reject(() => new PixelRoi(2, -1, 13, 13).Validate(16, 16), "Negative column refused");
        Reject(() => new PixelRoi(8, 2, 7, 13).Validate(16, 16), "Reversed rows refused");
        Reject(() => new PixelRoi(2, 8, 13, 7).Validate(16, 16), "Reversed columns refused");
        Reject(() => new PixelRoi(2, 2, 16, 13).Validate(16, 16), "Row equal to height refused");
        Reject(() => new PixelRoi(2, 2, 13, 16).Validate(16, 16), "Column equal to width refused");
        Reject(() => recipe.Validate(0, 16), "Zero image width refused");
        Reject(() => recipe.Validate(16, -1), "Negative image height refused");
        Reject(() => recipe.Validate(2000, 2000), "Teaching image cap enforced");
        Reject(() => (recipe with { Low = -1 }).Validate(16, 16), "Low below byte range refused");
        Reject(() => (recipe with { High = 256 }).Validate(16, 16), "High above byte range refused");
        Reject(() => (recipe with { Low = 201, High = 200 }).Validate(16, 16), "Reversed gray interval refused");
        (recipe with { Low = 0, High = 0 }).Validate(16, 16);
        (recipe with { Low = 255, High = 255 }).Validate(16, 16);
        Check(true, "Single-valued endpoint intervals accepted");
        Reject(() => (recipe with { OpeningWidth = 0 }).Validate(16, 16), "Zero opening width refused");
        Reject(() => (recipe with { OpeningHeight = 2 }).Validate(16, 16), "Even kernel refused by teaching policy, not HALCON restriction");
        Reject(() => (recipe with { OpeningWidth = 9 }).Validate(16, 16), "Kernel beyond teaching limit refused");
        Reject(() => (recipe with { OpeningHeight = -3 }).Validate(16, 16), "Negative opening height refused");
        (recipe with { OpeningWidth = 1, OpeningHeight = 7 }).Validate(16, 16);
        Check(true, "Independent odd width/height endpoints accepted");
        Reject(() => (recipe with { MinimumArea = 0 }).Validate(16, 16), "Nonpositive minimum area refused");
        Reject(() => (recipe with { MinimumArea = 61 }).Validate(16, 16), "Reversed area interval refused");
        Reject(() => (recipe with { MaximumArea = 145 }).Validate(16, 16), "Area cannot exceed inclusive ROI area");
        (recipe with { MinimumArea = 144, MaximumArea = 144 }).Validate(16, 16);
        Check(true, "Exact ROI area limit accepted");
        SegmentationRecipe noFill = recipe with { FillHoles = false };
        Check(recipe.FillHoles && !noFill.FillHoles && recipe.Roi == noFill.Roi, "Recipe copy does not mutate original");
        Check(new PixelRoi(0, 0, int.MaxValue, int.MaxValue).Area == 4_611_686_018_427_387_904L,
            "ROI area arithmetic uses long before subtraction/multiplication");
        Console.WriteLine($"Segmentation parameter checks passed: {count}. No image segmentation or HALCON native operator was invoked.");
    }
}
