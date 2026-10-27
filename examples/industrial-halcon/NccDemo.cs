using HalconDotNet;

namespace IndustrialHalcon;

public static class NccDemo
{
    private static double[] Numbers(HTuple tuple)
    {
        using HTuple real = tuple.TupleReal();
        return BlobReadback.CopyUsedValues(real.DArr, real.Length);
    }
    private static HObject Image(int rowShift, int columnShift)
    {
        int[] pixels = NccFixture.Pixels(rowShift, columnShift);
        using HTuple rows = new(Enumerable.Range(0, pixels.Length).Select(i => i / 64).ToArray());
        using HTuple columns = new(Enumerable.Range(0, pixels.Length).Select(i => i % 64).ToArray());
        using HTuple gray = new(pixels);
        HOperatorSet.GenImageConst(out HObject image, "byte", 64, 64);
        try
        {
            // Only paint the newly owned image, BEFORE any ReduceDomain/CopyObj aliases or model creation.
            // Deliberately tiny synthetic fixture; not a production high-throughput camera buffer adapter.
            HOperatorSet.SetGrayval(image, rows, columns, gray);
            return image;
        }
        catch { image.Dispose(); throw; }
    }
    private static NccReport Find(HObject image, HTuple model, PixelRoi search)
    {
        search.Validate(64, 64);
        HObject? roi = null, reduced = null;
        HTuple? rows = null, columns = null, angles = null, scores = null;
        try
        {
            HOperatorSet.GenRectangle1(out roi, search.Row1, search.Column1, search.Row2, search.Column2);
            HOperatorSet.ReduceDomain(image, roi, out reduced);
            // Fixed creation/search range: zero rotation. Ask for TWO, not a claim of uniqueness from top-1.
            HOperatorSet.FindNccModel(reduced, model, 0, 0, 0.99, 2, 0.5, "false", 1,
                out rows, out columns, out angles, out scores);
            return NccReadback.FromArrays(0.99, 2, Numbers(rows), Numbers(columns), Numbers(angles), Numbers(scores));
        }
        finally
        {
            scores?.Dispose(); angles?.Dispose(); columns?.Dispose(); rows?.Dispose();
            reduced?.Dispose(); roi?.Dispose();
        }
    }
    private static void Expected(NccReport report, double row, double column)
    {
        if (report.Outcome != NccOutcome.Candidates || report.Candidates.Count != 1 ||
            Math.Abs(report.Candidates[0].RawRowPixels - row) > 0.75 ||
            Math.Abs(report.Candidates[0].RawColumnPixels - column) > 0.75)
            throw new InvalidOperationException("Native NCC result differs from the synthetic translation expectation.");
    }
    public static void Run(bool checkExpected)
    {
        HObject? template = null, modelRoi = null, reducedTemplate = null, searchImage = null;
        HTuple? model = null;
        try
        {
            template = Image(0, 0);
            PixelRoi r = NccFixture.TemplateRoi;
            HOperatorSet.GenRectangle1(out modelRoi, r.Row1, r.Column1, r.Row2, r.Column2);
            HOperatorSet.ReduceDomain(template, modelRoi, out reducedTemplate);
            HOperatorSet.CreateNccModel(reducedTemplate, 1, 0, 0, 0.05, "use_polarity", out model);
            // Offset relative to the template domain centroid, NOT absolute source-image coordinates.
            HOperatorSet.SetNccModelOrigin(model, 0, 0);
            searchImage = Image(18, 14);
            NccReport found = Find(searchImage, model, new(16, 16, 55, 55));
            Console.WriteLine($"Native NCC outcome={found.Outcome}; returned candidates={found.Candidates.Count}; not uniqueness or product acceptance.");
            foreach (NccCandidate p in found.Candidates)
                Console.WriteLine($"raw pose row={p.RawRowPixels}, column={p.RawColumnPixels}, angle={p.AngleRadians} rad, score={p.Score}");
            if (!checkExpected) return;
            Expected(found, 41.5, 39.5);
            Expected(Find(template, model, new(16, 16, 55, 55)), 23.5, 25.5);
            // Center in a tiny ROI, while the entire patch extends beyond that ROI (but remains in image).
            Expected(Find(searchImage, model, new(40, 38, 43, 41)), 41.5, 39.5);
            NccReport excluded = Find(searchImage, model, new(0, 0, 5, 5));
            if (excluded.Outcome != NccOutcome.NoMatch || excluded.Candidates.Count != 0)
                throw new InvalidOperationException("Excluded / non-fitting reference-point search must have no result.");
            Console.WriteLine("Four actual HALCON NCC fixture cases passed; translation-only synthetic expectations, not rotation/dataset validation.");
        }
        finally
        {
            // Fixed package HTuple.Dispose releases stored handle ownership. Do not force-clear shared aliases.
            // No borrowed model, concurrent find, cache or explicit ClearNccModel in this single-owner example.
            model?.Dispose();
            searchImage?.Dispose(); reducedTemplate?.Dispose(); modelRoi?.Dispose(); template?.Dispose();
        }
    }
}
