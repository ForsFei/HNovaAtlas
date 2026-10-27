using System.Globalization;
using System.Text.Json;
using IndustrialYolo;

try
{
    if (args is ["--self-test"])
    {
        Checks.Run();
        return 0;
    }
    if (args is ["--preprocess-test"])
    {
        PreprocessingChecks.Run();
        return 0;
    }
    if (args is ["--preprocess-demo"])
    {
        PreprocessingDemo.Run();
        return 0;
    }
    if (args is ["--evaluate-fixture"] or ["--evaluate-fixture", _])
    {
        string path = args.Length == 2 ? args[1] : Path.Combine(AppContext.BaseDirectory, "fixtures", "handwritten-boxes.json");
        AuditedDataset data = FixtureJson.Read(path);
        EvaluationReport report = DetectionEvaluator.Evaluate(data);
        Console.WriteLine("HANDWRITTEN boxes only. No model loaded, no inference, no production-quality claim.");
        Console.WriteLine($"Labels: {string.Join(", ", data.Labels)}; test images: {data.Images.Count(image => image.Split == DatasetSplit.Test)}");
        Console.WriteLine($"TP={report.TruePositive} FP={report.FalsePositive} FN={report.FalseNegative} Filtered={report.Filtered}");
        Console.WriteLine($"Precision={Format(report.Precision)} Recall={Format(report.Recall)}; NOT AP/mAP.");
        foreach (PredictionDecision decision in report.Decisions)
            Console.WriteLine($"prediction[{decision.PredictionIndex}] {decision.ImageId}: {decision.Decision}, truth={decision.TruthIndex?.ToString(CultureInfo.InvariantCulture) ?? "-"}, matchedIoU={Format(decision.MatchedIou)}");
        return 0;
    }
    Console.Error.WriteLine("Usage: --self-test | --evaluate-fixture [local-json-path] | --preprocess-test | --preprocess-demo");
    return 2;
}
catch (Exception error) when (error is ArgumentException or JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
{
    Console.Error.WriteLine($"FAILED: {error.GetType().Name}: {error.Message}");
    return 1;
}

static string Format(double? value) => value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "undefined";
