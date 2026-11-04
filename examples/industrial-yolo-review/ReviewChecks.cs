using System.Text;
using System.Text.Json;
using IndustrialYolo;
using IndustrialYolo.Onnx;

namespace IndustrialYolo.Review;

internal static class ReviewChecks
{
    private static int count;
    public static void Managed()
    {
        count = 0;
        IReadOnlyList<Truth> Parse(string s) => ReviewData.ParseLabels(Encoding.UTF8.GetBytes(s), 100, 50);
        Check(Parse("0 .5 .5 .2 .4")[0] == new Truth(0, new(40, 15, 60, 35)), "normalized original xyxy");
        Check(Parse("79 .5 .5 1 1")[0].Box == new Box(0, 0, 100, 50), "full-image continuous edges");
        Check(Parse("").Count == 0 && Parse("\r\n \t").Count == 0, "explicit empty negative");
        Check(Parse("\uFEFF0\t.5 .5 .2 .2\r\n").Count == 1, "BOM/tab/CRLF label input");
        foreach (string invalid in new[] { "80 .5 .5 .2 .2", "-1 .5 .5 .2 .2", "0.0 .5 .5 .2 .2", "0 .5 .5 .2 .2 .9",
            "0 .5 .5 .2", "0 NaN .5 .2 .2", "0 .5 .5 0 .2", "0 .01 .5 .2 .2", "0 1.1 .5 .2 .2", "0 0,5 .5 .2 .2" })
            Reject(() => Parse(invalid), "invalid label row");
        Reject(() => ReviewData.ParseLabels([255], 100, 50), "invalid UTF8");
        Reject(() => Parse(string.Join('\n', Enumerable.Repeat("0 .5 .5 .2 .2", 129))), "truth count limit");
        Truth[] truth = [new(0, new(10, 10, 20, 20))];
        ReviewPrediction P(int row, int cls = 0, float score = .9f, Box? box = null) => new(row, cls, score, box ?? new(10, 10, 20, 20), false);
        CaseEvaluation match = Matching.Evaluate(100, 50, truth, [P(0)], .5);
        Check(match.Counts == new Counts(1, 0, 0) && match.Decisions[0].MatchedIou == 1, "perfect match");
        CaseEvaluation duplicate = Matching.Evaluate(100, 50, truth, [P(2, score: .8f), P(1)], .5);
        Check(duplicate.Counts == new Counts(1, 1, 0) && duplicate.Decisions[0].Prediction.Row == 1, "duplicate prediction FP and score order");
        Check(Matching.Evaluate(100, 50, truth, [P(2), P(1)], .5).Decisions[0].Prediction.Row == 1, "score tie stable row order");
        Check(Matching.Evaluate(100, 50, truth, [P(0, 1)], .5).Counts == new Counts(0, 1, 1), "wrong class FP and FN");
        Check(Matching.Evaluate(100, 50, truth, [], .5).MissedTruth.SequenceEqual(new[] { 0 }), "missing truth ledger");
        Check(Matching.Evaluate(100, 50, [], [P(0)], .5).Counts == new Counts(0, 1, 0), "negative image FP");
        Check(Matching.Evaluate(100, 50, [], [], .5).Counts is { Precision: null, Recall: null }, "undefined denominators");
        CaseEvaluation padding = Matching.Evaluate(100, 50, truth, [new(0, 0, .9f, null, true)], .5);
        Check(padding.Counts == new Counts(0, 0, 1) && padding.Decisions[0].Decision == "ExcludedPadding", "padding recorded outside matching");
        Check(Matching.Evaluate(100, 50, truth, [P(0, box: new(10, 10, 15, 20))], .5).Counts.Tp == 1, "IoU equals threshold accepted");
        Check(Matching.Evaluate(100, 50, [truth[0], truth[0]], [P(0)], .5).Decisions[0].TruthIndex == 0, "truth tie lower index");
        Reject(() => Matching.Evaluate(100, 50, truth, [P(0), P(0)], .5), "duplicate row");
        Reject(() => Matching.Evaluate(100, 50, truth, [P(0, score: float.NaN)], .5), "nonfinite score");
        Reject(() => Matching.Evaluate(100, 50, truth, [], 0), "invalid match threshold");
        Reject(() => Matching.Evaluate(0, 50, [], [], .5), "empty metrics still validate dimensions");

        string directory = DemoFiles.DirectoryForRun();
        byte[] image = [1, 2, 3], labels = Encoding.UTF8.GetBytes("0 .5 .5 .2 .2");
        DemoFiles.Write(Path.Combine(directory, "image.bin"), image); DemoFiles.Write(Path.Combine(directory, "labels.txt"), labels);
        ReviewCase item = new("one", "part-a", "image.bin", "labels.txt", 100, 50, ReviewData.Sha(image), ReviewData.Sha(labels));
        ReviewManifest manifest = new(ReviewData.Schema, "v1", "a1", "synthetic-check", "validation", ReviewData.LabelSet,
            ModelArtifact.Sha256, ReviewData.Pipeline, .1f, .45, .5, [item]);
        int number = 0;
        string Save(ReviewManifest value)
        { string path = Path.Combine(directory, $"manifest-{++number}.json"); DemoFiles.Write(path, JsonSerializer.SerializeToUtf8Bytes(value, ReviewData.Json)); return path; }
        LoadedReview loaded = ReviewData.Load(Save(manifest));
        Check(loaded.Cases.Count == 1 && loaded.Cases[0].Truth.Count == 1, "metadata/annotation audit without image decode");
        Reject(() => ReviewData.Load(Save(manifest with { ModelSha256 = new('0', 64) })), "model contract mismatch");
        Reject(() => ReviewData.Load(Save(manifest with { LabelSet = "scratch-dent" })), "label identity mismatch");
        Reject(() => ReviewData.Load(Save(manifest with { Partition = "train" })), "evaluation partition only");
        Reject(() => ReviewData.Load(Save(manifest with { Cases = [item, item] })), "duplicate case");
        Reject(() => ReviewData.Load(Save(manifest with { Cases = [item with { LabelsSha256 = new('0', 64) }] })), "label hash mismatch");
        Reject(() => ReviewData.Load(Save(manifest with { Cases = [item with { Labels = "missing.txt" }] })), "missing label not negative");
        foreach (string path in new[] { "../outside.png", "sub/../../outside.png", "C:/image.png", "sub\\image.png", ".. /image.png" })
            Reject(() => ReviewData.Resolve(directory, path), "relative path boundary");
        string unknown = JsonSerializer.Serialize(manifest, ReviewData.Json).TrimEnd();
        unknown = unknown[..^1] + ",\"unknown\":1}";
        string unknownPath = Path.Combine(directory, "unknown.json"); DemoFiles.Write(unknownPath, Encoding.UTF8.GetBytes(unknown));
        Reject(() => ReviewData.Load(unknownPath), "unknown JSON field");
        Console.WriteLine($"PASS {count} handwritten label/metric/manifest checks. No image decode or inference.");
    }
    public static void Native(string model)
    {
        count = 0; string directory = DemoFiles.DirectoryForRun(), path = DemoFiles.Create(directory);
        LoadedReview loaded = ReviewData.Load(path); ReviewReport report = ReviewRunner.Run(loaded, model);
        Check(report.Images.Count == 2 && report.DatasetKind == "synthetic-check", "two owned images through actual CPU");
        Check(report.Images[0].InputSha256 == "E4964C41C78FA76F8EF08D8AE0120C600209788B12F040140BF05F62BD750907", "old entire input fingerprint");
        Check(report.Images[0].Evaluation.Decisions.Select(d => d.Prediction.Row).SequenceEqual(new[] { 3439, 3468, 3462, 3454, 3420 }), "fixed gradient output rows");
        Check(report.Images[0].Evaluation.Counts == new Counts(0, 5, 1), "arbitrary class0 truth is NOT model-derived GT");
        Check(report.Classes.Count == 80 && report.Groups.Count == 2 && report.Images[1].Truth.Count == 0, "per-class/group and explicit negative");
        Check(report.Total.Tp == report.Classes.Sum(c => c.Counts.Tp) && report.Total.Fp == report.Groups.Sum(g => g.Counts.Fp), "aggregate balances");
        string output = Path.Combine(directory, "report.json"); ReviewRunner.Publish(report, output);
        using (JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(output)))
            Check(new FileInfo(output).Length > 0 && json.RootElement.GetProperty("images").GetArrayLength() == 2, "actual JSON artifact");
        byte[] previous = File.ReadAllBytes(output);
        Reject(() => ReviewRunner.Publish(report, output), "no report overwrite");
        Check(File.ReadAllBytes(output).SequenceEqual(previous) && Directory.GetFiles(directory, "*.tmp").Length == 0, "old artifact preserved and no temporary left");
        ReviewManifest invalid = loaded.Manifest with { Cases = [loaded.Manifest.Cases[0] with { Width = 640 }] };
        LoadedReview badSize = LoadVariant(invalid, directory, "wrong-size.json");
        Reject(() => ReviewRunner.Run(badSize, model), "decoded dimensions checked");
        LoadedReview badHash = LoadVariant(loaded.Manifest with { Cases = [loaded.Manifest.Cases[0] with { ImageSha256 = new('0', 64) }] }, directory, "wrong-hash.json");
        Reject(() => ReviewRunner.Run(badHash, model), "actual encoded bytes checked");
        string copy = Path.Combine(directory, "copy.png"); DemoFiles.Write(copy, File.ReadAllBytes(Path.Combine(directory, "色彩图.png")));
        LoadedReview duplicates = LoadVariant(loaded.Manifest with { Cases = [loaded.Manifest.Cases[0], loaded.Manifest.Cases[1] with
            { Image = "copy.png", Width = 641, Height = 359, ImageSha256 = loaded.Manifest.Cases[0].ImageSha256 }] }, directory, "duplicate-content.json");
        Reject(() => ReviewRunner.Run(duplicates, model), "duplicate image content rejects batch");
        Console.WriteLine($"PASS {count} actual OpenCV/CPU/review/artifact checks. Synthetic cases, not quality validation.");
        Console.WriteLine($"TP={report.Total.Tp} FP={report.Total.Fp} FN={report.Total.Fn}\nDEMO_MANIFEST={path}\nREPORT={output}");
    }
    private static LoadedReview LoadVariant(ReviewManifest manifest, string dir, string name)
    { string path = Path.Combine(dir, name); DemoFiles.Write(path, JsonSerializer.SerializeToUtf8Bytes(manifest, ReviewData.Json)); return ReviewData.Load(path); }
    private static void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException($"Review check failed: {name}"); count++; }
    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or JsonException or FormatException or OverflowException)
        { count++; return; }
        throw new InvalidOperationException($"Expected review rejection: {name}");
    }
}
