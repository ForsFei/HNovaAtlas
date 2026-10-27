using HalconDotNet;

namespace IndustrialHalcon;

public static class SegmentationDemo
{
    private static double[] Numbers(HTuple tuple)
    {
        using HTuple real = tuple.TupleReal();
        return BlobReadback.CopyUsedValues(real.DArr, real.Length);
    }
    private static void RequireSystem(string name, string expected)
    {
        HOperatorSet.GetSystem(name, out HTuple value);
        using (value)
            if (value.Length != 1 || value.S != expected)
                throw new InvalidOperationException($"Isolated run requires {name}={expected}; no settings were changed.");
    }
    private static void RequireSystem(string name, int expected)
    {
        HOperatorSet.GetSystem(name, out HTuple value);
        using (value)
            if (value.Length != 1 || value.I != expected)
                throw new InvalidOperationException($"Isolated run requires {name}={expected}; no settings were changed.");
    }
    private static double Area(HObject regions)
    {
        if (regions.CountObj() == 0) return 0;
        HTuple? area = null, row = null, column = null;
        try
        {
            HOperatorSet.AreaCenter(regions, out area, out row, out column);
            double[] values = Numbers(area);
            if (values.Length != regions.CountObj() || values.Any(a => !double.IsFinite(a) || a < 0 || a != Math.Truncate(a)))
                throw new InvalidOperationException("Invalid stage area output.");
            return values.Sum();
        }
        finally { column?.Dispose(); row?.Dispose(); area?.Dispose(); }
    }
    public static SegmentationTrace Analyze(string path, SegmentationRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        recipe.Validate(16, 16);
        if (!File.Exists(path) || new FileInfo(path).Length > 1_048_576)
            throw new ArgumentException("Bounded existing fixture required.");
        HObject? image = null, roi = null, reduced = null, threshold = null, opened = null, filled = null;
        HObject? clipped = null, connected = null, selected = null;
        HTuple? width = null, height = null, channels = null, type = null, areas = null, rows = null, columns = null;
        try
        {
            RequireSystem("neighborhood", 8);
            RequireSystem("max_connection", 0);
            RequireSystem("no_object_result", "true");
            RequireSystem("empty_region_result", "true");
            RequireSystem("store_empty_region", "true");
            HOperatorSet.ReadImage(out image, path);
            if (image.CountObj() != 1) throw new InvalidOperationException("One image required.");
            HOperatorSet.GetImageSize(image, out width, out height);
            HOperatorSet.CountChannels(image, out channels);
            HOperatorSet.GetImageType(image, out type);
            if (width.Length != 1 || height.Length != 1 || channels.Length != 1 || type.Length != 1 ||
                width.I != 16 || height.I != 16 || channels.I != 1 || type.S != "byte")
                throw new InvalidOperationException("Only the original 16x16 single-channel byte fixture is supported.");

            PixelRoi r = recipe.Roi;
            // Scalar numeric/string HTuple conversions are control parameters, not native handles.
            HOperatorSet.GenRectangle1(out roi, r.Row1, r.Column1, r.Row2, r.Column2);
            HOperatorSet.ReduceDomain(image, roi, out reduced);
            HOperatorSet.Threshold(reduced, out threshold, recipe.Low, recipe.High);
            double thresholdArea = Area(threshold);
            // A successful empty threshold is not a HALCON exception and is not product OK.
            if (thresholdArea == 0)
                return new(0, 0, 0, 0, 0, BlobReadback.FromArrays(16, 16, 0,
                    recipe.MinimumArea, recipe.MaximumArea, [], [], []));
            HOperatorSet.OpeningRectangle1(threshold, out opened, recipe.OpeningWidth, recipe.OpeningHeight);
            double openedArea = Area(opened);
            if (recipe.FillHoles) HOperatorSet.FillUp(opened, out filled);
            else HOperatorSet.CopyObj(opened, out filled, 1, -1); // Shared storage, never mutate either alias.
            double filledArea = Area(filled);
            // Region morphology does not carry a persistent image ROI constraint.
            HOperatorSet.Intersection(filled, roi, out clipped);
            double clippedArea = Area(clipped);
            HOperatorSet.Connection(clipped, out connected);
            int componentCount = connected.CountObj();
            using HTuple features = new(new string[] { "area", "width", "height" });
            using HTuple minima = new(new double[] { recipe.MinimumArea, 3, 3 });
            using HTuple maxima = new(new double[] { recipe.MaximumArea, 10, 10 });
            HOperatorSet.SelectShape(connected, out selected, features, "and", minima, maxima);
            int count = selected.CountObj();
            BlobReport report;
            if (count == 0) report = BlobReadback.FromArrays(16, 16, 0,
                recipe.MinimumArea, recipe.MaximumArea, [], [], []);
            else
            {
                if (count > 64) throw new InvalidOperationException("Teaching output cap exceeded.");
                HOperatorSet.AreaCenter(selected, out areas, out rows, out columns);
                report = BlobReadback.FromArrays(16, 16, count, recipe.MinimumArea, recipe.MaximumArea,
                    Numbers(areas), Numbers(rows), Numbers(columns));
            }
            return new(thresholdArea, openedArea, filledArea, clippedArea, componentCount, report);
        }
        finally
        {
            columns?.Dispose(); rows?.Dispose(); areas?.Dispose();
            selected?.Dispose(); connected?.Dispose(); clipped?.Dispose(); filled?.Dispose(); opened?.Dispose();
            threshold?.Dispose(); reduced?.Dispose(); roi?.Dispose(); image?.Dispose();
            type?.Dispose(); channels?.Dispose(); height?.Dispose(); width?.Dispose();
        }
    }
    private static void Check(SegmentationTrace trace, double threshold, double opened, double filled,
        int components, double? selectedArea)
    {
        bool candidateMatches = selectedArea is { } a
            ? trace.Selected.Outcome == BlobOutcome.Candidates && trace.Selected.Candidates.Count == 1 &&
              trace.Selected.Candidates[0] == new BlobFeature(a, 7, 7)
            : trace.Selected.Outcome == BlobOutcome.NoCandidates && trace.Selected.Candidates.Count == 0;
        if (trace.ThresholdArea != threshold || trace.OpenedArea != opened || trace.FilledArea != filled ||
            trace.ClippedArea != filled || trace.Components != components || !candidateMatches)
            throw new InvalidOperationException("Native morphology result differs from hand-calculated fixture expectation.");
    }
    public static void Run(bool checkExpected)
    {
        string file = Path.Combine(AppContext.BaseDirectory, "fixtures", "region-target.pgm");
        SegmentationRecipe recipe = SegmentationRecipe.Default;
        SegmentationTrace trace = Analyze(file, recipe);
        Console.WriteLine($"Native mask pixel counts: threshold={trace.ThresholdArea}, opened={trace.OpenedArea}, filled={trace.FilledArea}, clipped={trace.ClippedArea}; components={trace.Components}, selected={trace.Selected.Candidates.Count}");
        foreach (BlobFeature b in trace.Selected.Candidates)
            Console.WriteLine($"Processed mask area={b.AreaPixels} px; original-image row={b.RowPixels}, column={b.ColumnPixels}; not product OK/NG.");
        if (!checkExpected) return;
        Check(trace, 49, 48, 49, 1, 49);
        Check(Analyze(file, recipe with { FillHoles = false }), 49, 48, 48, 1, 48);
        Check(Analyze(file, recipe with { OpeningWidth = 1, OpeningHeight = 1 }), 49, 49, 50, 2, 49);
        Check(Analyze(file, recipe with { Low = 230 }), 0, 0, 0, 0, null);
        Console.WriteLine("Four actual HALCON ROI/morphology fixture cases passed. Not camera or product acceptance.");
    }
}
