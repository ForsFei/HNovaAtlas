using HalconDotNet;

namespace IndustrialHalcon;

public static class HalconApiDemo
{
    private static double[] Numbers(HTuple tuple)
    {
        using HTuple real = tuple.TupleReal();
        return BlobReadback.CopyUsedValues(real.DArr, real.Length);
    }
    private static int ScalarInteger(HTuple value)
    {
        if (value.Length != 1) throw new InvalidOperationException("Expected scalar tuple.");
        return value.I;
    }
    public static BlobReport Analyze(string path, int lowerGray, int upperGray, out (int ThresholdObjects, int Components) counts)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 1_048_576 || lowerGray < 0 || upperGray > 255 || lowerGray > upperGray)
            throw new ArgumentException("Existing bounded teaching file and byte threshold interval required.");
        HObject? image = null, region = null, components = null, selected = null;
        HTuple? width = null, height = null, channels = null, type = null, neighborhood = null, maximumConnections = null;
        HTuple? feature = null, operation = null, minArea = null, maxArea = null, low = null, high = null;
        HTuple? areas = null, rows = null, columns = null;
        try
        {
            // Read process settings, never silently change them. Other callers could still change them concurrently.
            HOperatorSet.GetSystem("neighborhood", out neighborhood);
            HOperatorSet.GetSystem("max_connection", out maximumConnections);
            if (ScalarInteger(neighborhood) != 8 || ScalarInteger(maximumConnections) != 0)
                throw new InvalidOperationException("This isolated teaching run requires neighborhood=8 and max_connection=0.");
            HOperatorSet.ReadImage(out image, path);
            if (image.CountObj() != 1) throw new InvalidOperationException("Exactly one image required.");
            HOperatorSet.GetImageSize(image, out width, out height);
            HOperatorSet.CountChannels(image, out channels);
            HOperatorSet.GetImageType(image, out type);
            int w = ScalarInteger(width), h = ScalarInteger(height);
            if (w != 8 || h != 6 || ScalarInteger(channels) != 1 || type.Length != 1 || type.S != "byte")
                throw new InvalidOperationException("This fixture test supports only one 8x6 byte channel, not arbitrary industrial images.");
            low = new(lowerGray); high = new(upperGray);
            HOperatorSet.Threshold(image, out region, low, high);
            HOperatorSet.Connection(region, out components);
            counts = (region.CountObj(), components.CountObj());
            feature = new("area"); operation = new("and"); minArea = new(2); maxArea = new(10);
            HOperatorSet.SelectShape(components, out selected, feature, operation, minArea, maxArea);
            int count = selected.CountObj();
            if (count == 0) return BlobReadback.FromArrays(w, h, 0, 2, 10, [], [], []);
            if (count > 64) throw new InvalidOperationException("Selected object teaching cap exceeded.");
            HOperatorSet.AreaCenter(selected, out areas, out rows, out columns);
            return BlobReadback.FromArrays(w, h, count, 2, 10, Numbers(areas), Numbers(rows), Numbers(columns));
        }
        finally
        {
            columns?.Dispose(); rows?.Dispose(); areas?.Dispose();
            high?.Dispose(); low?.Dispose(); maxArea?.Dispose(); minArea?.Dispose(); operation?.Dispose(); feature?.Dispose();
            selected?.Dispose(); components?.Dispose(); region?.Dispose(); image?.Dispose();
            maximumConnections?.Dispose(); neighborhood?.Dispose(); type?.Dispose(); channels?.Dispose(); height?.Dispose(); width?.Dispose();
        }
    }
    public static void Run(bool checkExpected)
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "contrast-target.pgm");
        BlobReport report = Analyze(fixture, 200, 255, out var counts);
        Console.WriteLine($"Native HALCON: threshold objects={counts.ThresholdObjects}, connected objects={counts.Components}, selected={report.Candidates.Count}, outcome={report.Outcome}");
        foreach (BlobFeature item in report.Candidates)
            Console.WriteLine($"area={item.AreaPixels} px, row={item.RowPixels}, column={item.ColumnPixels}");
        if (!checkExpected) return;
        if (counts != (1, 2) || report.Candidates.Count != 1 || report.Candidates[0] != new BlobFeature(4, 1.5, 1.5))
            throw new InvalidOperationException("Synthetic native fixture values differ from the documented expectation.");
        BlobReport empty = Analyze(fixture, 254, 255, out _);
        if (empty.Outcome != BlobOutcome.NoCandidates || empty.Candidates.Count != 0)
            throw new InvalidOperationException("One-pixel bright spot should be removed by area selection.");
        Console.WriteLine("Native fixture checks passed: two actual HALCON threshold/connection/selection cases. Not product accuracy acceptance.");
    }
}
