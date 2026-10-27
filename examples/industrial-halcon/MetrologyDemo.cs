using HalconDotNet;

namespace IndustrialHalcon;

public static class MetrologyDemo
{
    private static double[] Numbers(HTuple tuple, int cap = 64)
    {
        using HTuple real = tuple.TupleReal();
        if (real.Length > cap || real.DArr.Length < real.Length) throw new InvalidOperationException("Output tuple teaching cap/storage contract violated.");
        return real.DArr.AsSpan(0, real.Length).ToArray();
    }
    private static int Index(HTuple tuple)
    {
        if (tuple.Length != 1 || tuple.I is < 0 or > 31) throw new InvalidOperationException("One bounded object index required.");
        return tuple.I;
    }
    private static void Configure(HTuple model, HTuple index, int measures)
    {
        HOperatorSet.SetMetrologyObjectParam(model, index, "num_measures", measures);
        HOperatorSet.SetMetrologyObjectParam(model, index, "num_instances", 2);
        HOperatorSet.SetMetrologyObjectParam(model, index, "measure_transition", "all");
        HOperatorSet.SetMetrologyObjectParam(model, index, "measure_select", "all");
        HOperatorSet.SetMetrologyObjectParam(model, index, "measure_interpolation", "bilinear");
        HOperatorSet.SetMetrologyObjectParam(model, index, "min_score", 0.8);
        HOperatorSet.SetMetrologyObjectParam(model, index, "distance_threshold", 1.5);
        HOperatorSet.SetMetrologyObjectParam(model, index, "max_num_iterations", 500);
        HOperatorSet.SetMetrologyObjectParam(model, index, "rand_seed", 42);
    }
    private static (double[] Parameters, double[] Scores) Result(HTuple model, HTuple index)
    {
        HTuple? parameters = null, scores = null;
        try
        {
            HOperatorSet.GetMetrologyObjectResult(model, index, "all", "result_type", "all_param", out parameters);
            double[] copied = Numbers(parameters);
            // A successful empty geometry result has no instances; no need to request their scores.
            if (copied.Length == 0) return (copied, []);
            HOperatorSet.GetMetrologyObjectResult(model, index, "all", "result_type", "score", out scores);
            return (copied, Numbers(scores));
        }
        finally { scores?.Dispose(); parameters?.Dispose(); }
    }
    private static MetrologyDiagnostics Diagnostics(HTuple model, HTuple index, bool hasResults)
    {
        HObject? measures = null, result = null;
        HTuple? rows = null, columns = null;
        try
        {
            HOperatorSet.GetMetrologyObjectMeasures(out measures, model, index, "all", out rows, out columns);
            int resultCount = 0;
            if (hasResults)
            {
                HOperatorSet.GetMetrologyObjectResultContour(out result, model, index, "all", 1.0);
                resultCount = result.CountObj();
            }
            return MetrologyContracts.ReadDiagnostics(Index(index), measures.CountObj(), Numbers(rows, 256), Numbers(columns, 256), resultCount);
        }
        finally { columns?.Dispose(); rows?.Dispose(); result?.Dispose(); measures?.Dispose(); }
    }
    public static MetrologyReport Analyze(MetrologyScene scene)
    {
        int[] pixels = MetrologyFixture.Pixels(scene);
        using HTuple rows = new(Enumerable.Range(0, pixels.Length).Select(i => i / 128).ToArray());
        using HTuple columns = new(Enumerable.Range(0, pixels.Length).Select(i => i % 128).ToArray());
        using HTuple gray = new(pixels);
        using HTuple empty = new();
        HObject? image = null;
        HTuple? model = null, lineIndex = null, circleIndex = null;
        try
        {
            HOperatorSet.GenImageConst(out image, "byte", 128, 128);
            HOperatorSet.SetGrayval(image, rows, columns, gray); // New owned fixture image only.
            HOperatorSet.CreateMetrologyModel(out model);
            HOperatorSet.SetMetrologyModelImageSize(model, 128, 128); // Before adding objects.
            HOperatorSet.AddMetrologyObjectLineMeasure(model, 24, 40, 104, 40, 6, 3, 1, 20, empty, empty, out lineIndex);
            HOperatorSet.AddMetrologyObjectCircleMeasure(model, 64, 88, 20, 6, 2, 1, 20, empty, empty, out circleIndex);
            Configure(model, lineIndex, 20); Configure(model, circleIndex, 32);
            HOperatorSet.ApplyMetrologyModel(image, model);
            var lineValues = Result(model, lineIndex); var circleValues = Result(model, circleIndex);
            LineInstances lines = MetrologyContracts.ReadLines(Index(lineIndex), lineValues.Parameters, lineValues.Scores);
            CircleInstances circles = MetrologyContracts.ReadCircles(Index(circleIndex), circleValues.Parameters, circleValues.Scores);
            return MetrologyContracts.Combine(lines, circles,
                Diagnostics(model, lineIndex, lines.Instances.Count > 0), Diagnostics(model, circleIndex, circles.Instances.Count > 0));
        }
        finally
        {
            circleIndex?.Dispose(); lineIndex?.Dispose(); model?.Dispose(); image?.Dispose();
            // Index tuples are integers, not model owners. Model Dispose releases stored handle ownership in this package.
        }
    }
    private static void ExpectedBoth(MetrologyReport report)
    {
        if (report.Outcome != MetrologyOutcome.DimensionsMeasured || report.Dimensions is not { } d ||
            report.Lines.Instances.Count != 1 || report.Circles.Instances.Count != 1)
            throw new InvalidOperationException("One fitted line and circle required for fixture dimensions.");
        FittedLine line = report.Lines.Instances[0]; FittedCircle circle = report.Circles.Instances[0];
        if (Math.Abs(line.Begin.Column - 39.5) > 1 || Math.Abs(line.End.Column - 39.5) > 1 ||
            Math.Abs(circle.Center.Row - 64) > 1 || Math.Abs(circle.Center.Column - 88) > 1 || Math.Abs(circle.RadiusPixels - 20) > 1 ||
            Math.Abs(d.DiameterPixels - 40) > 2 || Math.Abs(d.CenterToInfiniteLinePixels - 48.5) > 2 ||
            Math.Abs(d.SignedCircleLineClearancePixels - 28.5) > 3 ||
            report.LineDiagnostics is not { ResultContourObjects: > 0 } || report.CircleDiagnostics is not { ResultContourObjects: > 0 })
            throw new InvalidOperationException("Native metrology result differs from synthetic teaching expectations.");
    }
    public static void Run(bool checkExpected)
    {
        MetrologyReport report = Analyze(MetrologyScene.Both);
        Console.WriteLine($"Native metrology: {report.Outcome}; object indices={report.Lines.ObjectIndex}/{report.Circles.ObjectIndex}, instances={report.Lines.Instances.Count}/{report.Circles.Instances.Count}; not product OK/NG.");
        if (report.Dimensions is { } d)
            Console.WriteLine($"Pixel diameter={d.DiameterPixels}, center-to-INFINITE-line={d.CenterToInfiniteLinePixels}, signed clearance={d.SignedCircleLineClearancePixels}; no calibration.");
        if (!checkExpected) return;
        ExpectedBoth(report);
        foreach (MetrologyScene scene in new[] { MetrologyScene.LineOnly, MetrologyScene.CircleOnly, MetrologyScene.Blank, MetrologyScene.SmallCircle })
        {
            MetrologyReport result = Analyze(scene);
            int expectedLines = scene == MetrologyScene.CircleOnly || scene == MetrologyScene.Blank ? 0 : 1;
            int expectedCircles = scene == MetrologyScene.CircleOnly ? 1 : 0;
            MetrologyOutcome expected = scene == MetrologyScene.Blank ? MetrologyOutcome.NoResult : MetrologyOutcome.MissingObject;
            if (result.Outcome != expected || result.Lines.Instances.Count != expectedLines || result.Circles.Instances.Count != expectedCircles || result.Dimensions is not null)
                throw new InvalidOperationException($"Native missing-object fixture failed: {scene}.");
        }
        Console.WriteLine("Five actual HALCON metrology fixture cases passed. No accuracy, residual, arbitrary-shape or production acceptance.");
    }
}
