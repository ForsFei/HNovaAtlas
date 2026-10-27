using System.Text.Json;

namespace IndustrialYolo;

internal static class Checks
{
    private static int count;
    public static void Run()
    {
        count = 0;
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "handwritten-boxes.json");
        string json = File.ReadAllText(path);
        AuditedDataset audited = FixtureJson.Read(path);
        EvaluationFixture baseline = new("handwritten-evaluation", audited.Labels, audited.Images,
            audited.Predictions, audited.ScoreThreshold, audited.IouThreshold);
        EvaluationReport report = DetectionEvaluator.Evaluate(audited);
        Check(audited.Images.Count == 4 && audited.Labels.SequenceEqual(["scratch", "dent"]), "actual JSON read and class order");
        Check(report.TruePositive == 1, "one true positive");
        Check(report.FalsePositive == 3, "duplicate/background/negative image false positives");
        Check(report.FalseNegative == 1, "filtered dent leaves missed truth");
        Check(report.Filtered == 1 && report.Decisions.Count == 5, "every supplied prediction accounted");
        Near(report.Precision, .25, "precision");
        Near(report.Recall, .5, "recall");
        Check(report.Decisions.Single(d => d.PredictionIndex == 1).Decision == Decision.FalsePositive, "duplicate not silently suppressed");
        Check(report.Decisions.Single(d => d.PredictionIndex == 2).Decision == Decision.Filtered, "score filter before matching");
        Check(report.Decisions.Single(d => d.PredictionIndex == 4).ImageId == "test-b", "negative image retained");
        Check(report.Decisions.Single(d => d.PredictionIndex == 0) is { TruthIndex: 0, MatchedIou: 1 }, "explicit match association");

        Box a = new(10, 10, 30, 30);
        Near(BoxMath.IoU(a, a), 1, "identical IoU");
        Near(BoxMath.IoU(a, new(30, 10, 50, 30)), 0, "touching edges have zero area");
        Near(BoxMath.IoU(a, new(40, 40, 50, 50)), 0, "disjoint IoU");
        Near(BoxMath.IoU(a, new(10, 10, 20, 30)), .5, "continuous half box, no +1");
        Near(BoxMath.IoU(a, new(11, 11, 31, 31)), 361d / 439, "duplicate IoU exact geometry");
        Near(BoxMath.IoU(a, new(11, 11, 31, 31)), BoxMath.IoU(new(11, 11, 31, 31), a), "IoU symmetric");
        Near(BoxMath.IoU(new(0, 0, 20, 20), new(1, 1, 21, 21)), 361d / 439, "IoU translation invariant");
        Reject(() => BoxMath.IoU(new(20, 0, 10, 10), a), "inverted box rejected");
        Reject(() => BoxMath.IoU(new(0, 0, 0, 10), a), "zero area rejected");
        Reject(() => BoxMath.IoU(new(double.NaN, 0, 10, 10), a), "NaN rejected");
        Reject(() => BoxMath.IoU(new(0, 0, double.PositiveInfinity, 10), a), "infinity rejected");
        Reject(() => BoxMath.IoU(new(-1, 0, 10, 10), a), "negative coordinate rejected");
        Reject(() => BoxMath.IoU(new(0, 0, 8193, 10), a), "maximum extent enforced");
        Reject(() => BoxMath.IoU(new(0, 0, double.Epsilon, double.Epsilon), a), "underflow area rejected");

        EvaluationReport high = Run(baseline with { ScoreThreshold = .85 });
        Check(high is { TruePositive: 1, FalsePositive: 1, FalseNegative: 1, Filtered: 3 }, "higher threshold operating point");
        EvaluationReport low = Run(baseline with { ScoreThreshold = .4 });
        Check(low is { TruePositive: 2, FalsePositive: 3, FalseNegative: 0, Filtered: 0 }, "score equality included");
        Near(low.Precision, .4, "lower threshold precision");
        Near(low.Recall, 1, "lower threshold recall");
        Prediction[] half = [new("test-a", 0, .5, new(10, 10, 20, 30))];
        Check(Run(baseline with { Predictions = half }).TruePositive == 1, "IoU equality included");
        Check(Run(baseline with { Predictions = half, IouThreshold = .5001 }).TruePositive == 0, "IoU just below threshold");
        Prediction[] wrongClass = [new("test-a", 1, .9, a)];
        Check(Run(baseline with { Predictions = wrongClass }) is { TruePositive: 0, FalsePositive: 1, FalseNegative: 2 }, "wrong class is FP and truth remains FN");
        Prediction[] wrongImage = [new("test-b", 0, .9, a)];
        Check(Run(baseline with { Predictions = wrongImage }) is { TruePositive: 0, FalsePositive: 1, FalseNegative: 2 }, "matching cannot cross images");
        Prediction[] reversedScores = [new("test-a", 0, .6, a), new("test-a", 0, .9, a)];
        Check(Run(baseline with { Predictions = reversedScores }).Decisions.First().PredictionIndex == 1, "descending score before input order");
        Prediction[] ties = [new("test-a", 0, .9, a), new("test-a", 0, .9, a)];
        EvaluationReport tied = Run(baseline with { Predictions = ties });
        Check(tied.Decisions[0].PredictionIndex == 0 && tied.Decisions[0].Decision == Decision.TruePositive && tied.FalsePositive == 1, "tie uses original prediction index, one-to-one");
        DatasetImage[] twoTruths = baseline.Images.Select(i => i.Id == "test-a" ? i with { Truth = new Truth[] { new(0, a), new(0, a) } } : i).ToArray();
        Check(Run(baseline with { Images = twoTruths, Predictions = ties }).TruePositive == 2, "two truths can receive distinct predictions");
        Check(Run(baseline with { Images = twoTruths, Predictions = ties }).Decisions[0].TruthIndex == 0, "truth IoU tie uses first unmatched index");
        EvaluationReport emptyPredictions = Run(baseline with { Predictions = [] });
        Check(emptyPredictions.Precision is null && emptyPredictions.Recall == 0 && emptyPredictions.FalseNegative == 2, "no predictions gives undefined precision");
        DatasetImage[] noTruth = baseline.Images.Select(i => i with { Truth = Array.Empty<Truth>() }).ToArray();
        EvaluationReport noTruthReport = Run(baseline with { Images = noTruth });
        Check(noTruthReport.Recall is null && noTruthReport.Precision == 0 && noTruthReport.FalsePositive == 4, "no truth gives undefined recall");
        EvaluationReport neither = Run(baseline with { Images = noTruth, Predictions = [] });
        Check(neither.Recall is null && neither.Precision is null, "both empty denominators undefined");

        Reject(() => FixtureJson.Parse(json.Replace("\"x1\":10,", "", StringComparison.Ordinal)), "required coordinate cannot default to zero");
        Reject(() => FixtureJson.Parse(json.Replace("\"fixtureKind\":", "\"unknown\":0,\"fixtureKind\":", StringComparison.Ordinal)), "unknown JSON property rejected");
        Reject(() => FixtureJson.Parse(json.Replace("\"split\":\"train\"", "\"split\":0", StringComparison.Ordinal)), "integer enum rejected");
        Reject(() => FixtureJson.Parse(json.Replace("\"split\":\"train\"", "\"split\":\"other\"", StringComparison.Ordinal)), "unknown split rejected");
        Reject(() => FixtureJson.Parse(json.Replace("\"score\":0.9", "\"score\":NaN", StringComparison.Ordinal)), "non-JSON NaN rejected");
        Reject(() => FixtureJson.Parse("null"), "null fixture rejected");
        Reject(() => FixtureJson.Parse(new string(' ', FixtureJson.MaximumBytes + 1)), "oversize JSON rejected");
        Reject(() => FixtureJson.Audit(baseline with { FixtureKind = "inference" }), "cannot claim fixture is model inference");
        Reject(() => FixtureJson.Audit(baseline with { Labels = ["scratch", "scratch"] }), "duplicate class names rejected");
        Reject(() => FixtureJson.Audit(baseline with { Labels = [" scratch", "dent"] }), "class whitespace rejected");
        Reject(() => FixtureJson.Audit(baseline with { Labels = null! }), "explicit null list rejected");
        Reject(() => FixtureJson.Audit(baseline with { Images = [baseline.Images[0], baseline.Images[0]] }), "duplicate image id rejected");
        Reject(() => FixtureJson.Audit(baseline with { Images = baseline.Images.Select(i => i.Id == "test-a" ? i with { Group = "batch-train" } : i).ToArray() }), "cross-split group leakage rejected");
        Reject(() => FixtureJson.Audit(baseline with { Images = baseline.Images.Where(i => i.Split != DatasetSplit.Validation).ToArray() }), "teaching manifest needs three splits");
        Reject(() => FixtureJson.Audit(baseline with { Images = baseline.Images.Select(i => i with { Width = 0 }).ToArray() }), "invalid image extent rejected");
        Reject(() => FixtureJson.Audit(baseline with { Images = baseline.Images.Select(i => i with { Width = 8192, Height = 8192 }).ToArray() }), "pixel budget enforced");
        Reject(() => FixtureJson.Audit(baseline with { Images = baseline.Images.Select(i => i.Id == "test-a" ? i with { Truth = new Truth[] { new(2, a) } } : i).ToArray() }), "invalid truth class rejected");
        Reject(() => FixtureJson.Audit(baseline with { Predictions = [new("unknown", 0, .5, a)] }), "unknown prediction image rejected");
        Reject(() => FixtureJson.Audit(baseline with { Predictions = [new("train-001", 0, .5, a)] }), "training predictions cannot enter test metric");
        Reject(() => FixtureJson.Audit(baseline with { Predictions = [new("test-a", 2, .5, a)] }), "invalid prediction class rejected");
        Reject(() => FixtureJson.Audit(baseline with { Predictions = [new("test-a", 0, 1.1, a)] }), "invalid score rejected");
        Reject(() => FixtureJson.Audit(baseline with { Predictions = [new("test-a", 0, .5, new(0, 0, 101, 20))] }), "image-specific box bounds enforced");
        Reject(() => FixtureJson.Audit(baseline with { Predictions = Enumerable.Repeat(ties[0], 257).ToArray() }), "per-image prediction bound enforced");
        Reject(() => FixtureJson.Audit(baseline with { ScoreThreshold = double.NaN }), "nonfinite score threshold rejected");
        Reject(() => FixtureJson.Audit(baseline with { IouThreshold = 0 }), "zero IoU threshold rejected");
        List<string> labels = ["scratch", "dent"];
        List<Truth> truths = [new(0, a), new(1, new(60, 50, 80, 70))];
        List<Prediction> predictions = baseline.Predictions.ToList();
        List<DatasetImage> images = baseline.Images.Select(i => i.Id == "test-a" ? i with { Truth = truths } : i).ToList();
        AuditedDataset detached = FixtureJson.Audit(baseline with { Labels = labels, Images = images, Predictions = predictions });
        labels.Clear(); truths.Clear(); predictions.Clear(); images.Clear();
        Check(detached.Labels.Count == 2 && detached.Images.Count == 4 && detached.Predictions.Count == 5 && detached.Images[2].Truth.Count == 2, "audit snapshots caller collections");
        Reject(() => ((IList<string>)detached.Labels)[0] = "changed", "labels exposed read-only");
        Reject(() => ((IList<PredictionDecision>)report.Decisions).Clear(), "decisions exposed read-only");
        Check(DetectionEvaluator.Evaluate(detached) is { TruePositive: 1, FalsePositive: 3, FalseNegative: 1 }, "snapshot result stable after caller mutation");
        Console.WriteLine($"PASS {count} handwritten-data/contract checks. No model/runtime/inference tested.");
    }

    private static EvaluationReport Run(EvaluationFixture fixture) => DetectionEvaluator.Evaluate(FixtureJson.Audit(fixture));
    private static void Near(double? actual, double expected, string name) => Check(actual.HasValue && Math.Abs(actual.Value - expected) <= 1e-12, name);
    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException($"Check failed: {name}");
        count++;
    }
    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or JsonException or NotSupportedException)
        { count++; return; }
        throw new InvalidOperationException($"Expected rejection: {name}");
    }
}
