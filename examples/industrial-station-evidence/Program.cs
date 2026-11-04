using IndustrialStationEvidence;
try
{
    if (args.Length > 0 && args[0] == "--inspect-images")
    {
        if (args.Length is not (5 or 8) || args.Skip(1).Take(4).Any(string.IsNullOrWhiteSpace))
            return PrintUsage();

        Guid session = Guid.Empty;
        long cycle = 0;
        bool singleCycle = args.Length == 8;
        if (singleCycle && (args[5] != "--cycle" || !Guid.TryParse(args[6], out session) || session == Guid.Empty ||
            !long.TryParse(args[7], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out cycle) || cycle <= 0))
            return PrintUsage();

        ImageEvidenceReport report = ImageEvidenceReconciler.Inspect(args[1], args[2], args[3], args[4]);
        var json = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        json.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        if (!singleCycle)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report, json));
            return report.RequiresAttention ? 3 : 0;
        }

        var baseRows = report.BaseReport.Rows.Where(row => row.Session == session && row.Cycle == cycle).ToArray();
        var images = report.Images.Where(row => row.Session == session && row.Cycle == cycle).ToArray();
        bool found = baseRows.Length != 0 || images.Length != 0;
        bool requiresAttention = report.RequiresAttention || !found;
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            Session = session,
            Cycle = cycle,
            Found = found,
            BaseRows = baseRows,
            Images = images,
            report.BaseReport.StorageIssues,
            report.ImageStorageIssues,
            BatchRequiresAttention = report.RequiresAttention,
            RequiresAttention = requiresAttention
        }, json));
        return requiresAttention ? 3 : 0;
    }
    if (args is ["--image-reconciliation-demo"]) { await ReconciliationChecks.ImageDemoAsync(); return 0; }
    if (args is ["--image-reconciliation-test"]) { ImageReconciliationChecks.Run(); return 0; }
    if (args is ["--self-test"]) { await EvidenceChecks.RunAsync(); return 0; }
    if (args is ["--reconciliation-test"]) { await ReconciliationChecks.RunAsync(); return 0; }
    if (args is ["--reconciliation-demo"]) { await ReconciliationChecks.DemoAsync(); return 0; }
    if (args is ["--demo"]) { await EvidenceChecks.DemoAsync(); return 0; }
    return PrintUsage();
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }


static int PrintUsage()
{
    Console.Error.WriteLine("Usage: --inspect-images <ledger> <normal> <abnormal> <images> [--cycle <session> <cycle>] | --image-reconciliation-demo | --image-reconciliation-test | --self-test | --demo | --reconciliation-test | --reconciliation-demo");
    return 2;
}
