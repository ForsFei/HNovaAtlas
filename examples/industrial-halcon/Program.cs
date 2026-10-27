using IndustrialHalcon;

if (args is not ["--contract-test"] and not ["--native-demo"] and not ["--native-test"] and
    not ["--segmentation-contract-test"] and not ["--segmentation-native-demo"] and not ["--segmentation-native-test"] and
    not ["--ncc-contract-test"] and not ["--ncc-native-demo"] and not ["--ncc-native-test"] and
    not ["--shape-contract-test"] and not ["--shape-native-demo"] and not ["--shape-native-test"] and
    not ["--measure-contract-test"] and not ["--measure-native-demo"] and not ["--measure-native-test"] and
    not ["--metrology-contract-test"] and not ["--metrology-native-demo"] and not ["--metrology-native-test"] and
    not ["--engine-contract-test"] and not ["--engine-native-demo"] and not ["--engine-native-test"])
{
    Console.Error.WriteLine("Use --contract-test or --segmentation-contract-test / --ncc-contract-test / --shape-contract-test / --measure-contract-test / --metrology-contract-test / --engine-contract-test (managed only); native demos/tests: --native-demo / --native-test, or --<segmentation|ncc|shape|measure|metrology|engine>-native-demo / --<segmentation|ncc|shape|measure|metrology|engine>-native-test (matching native runtime/license required).");
    return 2;
}
if (args[0] is "--contract-test" or "--segmentation-contract-test" or "--ncc-contract-test" or "--shape-contract-test" or "--measure-contract-test" or "--metrology-contract-test" or "--engine-contract-test")
{
    try
    {
        if (args[0] == "--contract-test") ContractChecks.Run();
        else if (args[0] == "--segmentation-contract-test") SegmentationChecks.Run();
        else if (args[0] == "--ncc-contract-test") NccChecks.Run();
        else if (args[0] == "--shape-contract-test") ShapeChecks.Run();
        else if (args[0] == "--measure-contract-test") MeasureChecks.Run();
        else if (args[0] == "--metrology-contract-test") MetrologyChecks.Run();
        else EngineContracts.Checks();
        return 0;
    }
    catch (Exception error) { Console.Error.WriteLine(error); return 1; }
}
try
{
    // No fallback implementation. Missing runtime/license is NOT a successful empty detection.
    if (args[0].StartsWith("--segmentation-", StringComparison.Ordinal))
        SegmentationDemo.Run(checkExpected: args[0] == "--segmentation-native-test");
    else if (args[0].StartsWith("--ncc-", StringComparison.Ordinal))
        NccDemo.Run(checkExpected: args[0] == "--ncc-native-test");
    else if (args[0].StartsWith("--shape-", StringComparison.Ordinal))
        ShapeDemo.Run(checkExpected: args[0] == "--shape-native-test");
    else if (args[0].StartsWith("--measure-", StringComparison.Ordinal))
        MeasureDemo.Run(checkExpected: args[0] == "--measure-native-test");
    else if (args[0].StartsWith("--metrology-", StringComparison.Ordinal))
        MetrologyDemo.Run(checkExpected: args[0] == "--metrology-native-test");
    else if (args[0].StartsWith("--engine-", StringComparison.Ordinal))
        EngineDemo.Run(checkExpected: args[0] == "--engine-native-test");
    else HalconApiDemo.Run(checkExpected: args[0] == "--native-test");
    return 0;
}
catch (Exception error)
{
    Exception cause = error;
    while (cause is TypeInitializationException && cause.InnerException is { } inner) cause = inner;
    Console.Error.WriteLine($"Native execution not passed: {cause.GetType().Name}. Verify matching HALCON 24.11.3 native runtime, x64 and valid license in the vendor-configured environment. No fallback or license change was attempted.");
    return 3;
}
