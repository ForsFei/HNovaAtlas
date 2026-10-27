using HalconDotNet;

namespace IndustrialHalcon;

public static class EngineDemo
{
    private static double[] Numbers(HTuple value)
    {
        using HTuple real = value.TupleReal();
        return BlobReadback.CopyUsedValues(real.DArr, real.Length);
    }
    public static BlobReport Analyze(double low)
    {
        EngineContracts.ValidateLow(low);
        HOperatorSet.ReadImage(out HObject image, Path.Combine(AppContext.BaseDirectory, "fixtures", "contrast-target.pgm"));
        using (image) return AnalyzeImage(image, low);
    }
    // Borrows one immutable image for this synchronous call. The caller retains ownership.
    public static BlobReport AnalyzeImage(HObject image, double low)
    {
        EngineContracts.ValidateLow(low);
        // Standalone CLI only: engine procedure path is process configuration, not per-request UI state.
        using HDevEngine engine = new();
        engine.SetProcedurePath(Path.Combine(AppContext.BaseDirectory, "procedures"));
        using HDevProcedure procedure = new("atlas_blob_v1");
        HDevProcedureCall? call = null;
        using HTuple threshold = new(low);
        HObject? selected = null;
        HTuple? version = null, areas = null, rows = null, columns = null;
        try
        {
            HOperatorSet.GetSystem("neighborhood", out HTuple neighborhood);
            using (neighborhood) if (neighborhood.I != 8) throw new InvalidOperationException("Fixture requires 8-neighborhood.");
            HOperatorSet.GetSystem("max_connection", out HTuple maximum);
            using (maximum) if (maximum.I != 0) throw new InvalidOperationException("Fixture requires unrestricted connection count.");
            HOperatorSet.GetImageSize(image, out HTuple width, out HTuple height);
            using (width) using (height)
                if (image.CountObj() != 1 || width.I != 8 || height.I != 6) throw new InvalidOperationException("One 8x6 fixture image required.");
            HOperatorSet.GetImageType(image, out HTuple type);
            using (type) if (type.S != "byte") throw new InvalidOperationException("Byte fixture required.");
            HOperatorSet.CountChannels(image, out HTuple channels);
            using (channels) if (channels.I != 1) throw new InvalidOperationException("Single-channel fixture required.");
            call = new(procedure);
            call.SetInputIconicParamObject("Image", image);
            call.SetInputCtrlParamTuple("Low", threshold);
            call.Execute();
            selected = call.GetOutputIconicParamObject("Selected");
            version = call.GetOutputCtrlParamTuple("ContractVersion");
            areas = call.GetOutputCtrlParamTuple("Areas");
            rows = call.GetOutputCtrlParamTuple("Rows");
            columns = call.GetOutputCtrlParamTuple("Columns");
            return EngineContracts.Read(Numbers(version), selected.CountObj(), Numbers(areas), Numbers(rows), Numbers(columns));
        }
        finally
        {
            columns?.Dispose(); rows?.Dispose(); areas?.Dispose(); version?.Dispose(); selected?.Dispose();
            // Input image remains alive until the call has been disposed.
            call?.Dispose();
        }
    }
    public static void Run(bool checkExpected)
    {
        BlobReport report = Analyze(200);
        Console.WriteLine($"Native engine outcome={report.Outcome}, candidates={report.Candidates.Count}; not product OK/NG.");
        if (!checkExpected) return;
        foreach (int low in new[] { 200, 254 })
        {
            BlobReport actual = low == 200 ? report : Analyze(low);
            string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "contrast-target.pgm");
            BlobReport direct = HalconApiDemo.Analyze(fixture, low, 255, out _);
            if (!actual.Candidates.SequenceEqual(direct.Candidates) ||
                low == 200 && (actual.Candidates.Count != 1 || actual.Candidates[0] != new BlobFeature(4, 1.5, 1.5)) ||
                low == 254 && actual.Outcome != BlobOutcome.NoCandidates)
                throw new InvalidOperationException("Engine/direct operator fixture comparison failed.");
        }
        Console.WriteLine("Two native engine cases and direct-operator comparisons passed; not HDevelop UI or production validation.");
    }
}
