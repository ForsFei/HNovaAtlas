using IndustrialYolo.Deployment;

try
{
    if (args is ["--self-test"]) { DeploymentChecks.Run(); return 0; }
    if (args is ["--seal", string root, string releaseId])
    { Console.WriteLine($"PROFILE={ReleaseInventory.Seal(root, releaseId)}"); return 0; }
    if (args is ["--inspect", _, _] or ["--probe", _, _])
    {
        DeploymentReport result = DeploymentProbe.Run(args[1], args[0] == "--probe");
        DeploymentProbe.WriteReport(result, args[2]);
        Console.WriteLine($"STATUS={result.Status}; RELEASE={result.ReleaseId}");
        foreach (ProbeStage stage in result.Stages) Console.WriteLine($"{stage.Name}: {stage.Status} {stage.Detail}");
        if (result.Smoke is { } smoke) Console.WriteLine($"SMOKE input={smoke.InputSha256} raw={smoke.OutputValues} samples={smoke.FrozenReferenceSamples} rows={smoke.DecodedRows} kept={smoke.Kept}");
        Console.WriteLine($"REPORT={Path.GetFullPath(args[2])}");
        return result.Status == "Failed" ? 3 : 0;
    }
    Console.Error.WriteLine("Usage: --self-test | --seal <release-folder> <release-id> | --inspect <release-profile.json> <new-report-outside-folder.json> | --probe <release-profile.json> <new-report-outside-folder.json>"); return 2;
}
catch (Exception error)
{
    Console.Error.WriteLine($"FAILED, no installation/path changes/fallback: {error.GetType().Name}: {error.Message}");
    return args.FirstOrDefault() == "--self-test" ? 1 : 4;
}
