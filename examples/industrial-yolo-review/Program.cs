using IndustrialYolo.Review;

bool native = args.FirstOrDefault() is "--native-test" or "--demo" or "--evaluate";
try
{
    if (args is ["--self-test"]) { ReviewChecks.Managed(); return 0; }
    if (args is ["--native-test", string model]) { ReviewChecks.Native(model); return 0; }
    if (args is ["--demo", _] or ["--evaluate", _, _, _])
    {
        string? directory = args[0] == "--demo" ? DemoFiles.DirectoryForRun() : null;
        string manifest = directory is null ? args[2] : DemoFiles.Create(directory);
        string destination = directory is null ? args[3] : Path.Combine(directory, "report.json");
        if (File.Exists(destination)) throw new IOException("Report exists; choose a new path.");
        ReviewReport report = ReviewRunner.Run(ReviewData.Load(manifest), args[1]);
        ReviewRunner.Publish(report, destination);
        Console.WriteLine($"DatasetKind={report.DatasetKind}; fixed operating point only, not AP/mAP or production OK/NG.");
        Console.WriteLine($"TP={report.Total.Tp} FP={report.Total.Fp} FN={report.Total.Fn} Precision={report.Total.Precision?.ToString("G6", System.Globalization.CultureInfo.InvariantCulture) ?? "undefined"} Recall={report.Total.Recall?.ToString("G6", System.Globalization.CultureInfo.InvariantCulture) ?? "undefined"}");
        Console.WriteLine($"MANIFEST={Path.GetFullPath(manifest)}\nREPORT={Path.GetFullPath(destination)}"); return 0;
    }
    Console.Error.WriteLine("Usage: --self-test | --native-test <model> | --demo <model> | --evaluate <model> <manifest.json> <new-report.json>"); return 2;
}
catch (Exception error)
{
    Console.Error.WriteLine($"FAILED, no partial report/fallback: {error.GetType().Name}: {error.Message}");
    return native ? 3 : 1;
}
