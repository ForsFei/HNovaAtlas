using IndustrialOpenCvSharp;

if (args is ["--stripe-contract-test"])
{
    try { StripeContracts.Run(); return 0; }
    catch (Exception error) { Console.Error.WriteLine(error); return 1; }
}

if (args is ["--edges-contract-test"])
{
    try { EdgeContracts.Checks(); return 0; }
    catch (Exception error) { Console.Error.WriteLine(error); return 1; }
}

if (args is ["--shapes-contract-test"])
{
    try { ShapeContracts.Checks(); return 0; }
    catch (Exception error) { Console.Error.WriteLine(error); return 1; }
}

if (args is ["--regions-contract-test"])
{
    try { RegionContracts.Checks(); return 0; }
    catch (Exception error) { Console.Error.WriteLine(error); return 1; }
}

if (args is ["--segmentation-contract-test"])
{
    try { SegmentationFixture.Checks(); return 0; }
    catch (Exception error) { Console.Error.WriteLine(error); return 1; }
}
if (args is ["--contract-test"])
{
    try { PixelLayout.Checks(); return 0; }
    catch (Exception error) { Console.Error.WriteLine(error); return 1; }
}
if (args is not ["--native-test"] and not ["--demo"] and not ["--segmentation-native-test"] and not ["--segmentation-demo"] and not ["--regions-native-test"] and not ["--regions-demo"] and not ["--shapes-native-test"] and not ["--shapes-demo"] and not ["--edges-native-test"] and not ["--edges-demo"] and not ["--stripe-native-test"] and not ["--stripe-demo"])
{
    Console.Error.WriteLine("Use --contract-test / --segmentation-contract-test / --regions-contract-test / --shapes-contract-test / --edges-contract-test / --stripe-contract-test (managed), --native-test / --demo, --segmentation-native-test / --segmentation-demo, --regions-native-test / --regions-demo, --shapes-native-test / --shapes-demo, --edges-native-test / --edges-demo, or --stripe-native-test / --stripe-demo (actual OpenCV). No device or fallback."); return 2;
}
try
{
    if (args is ["--stripe-native-test"]) StripeChecks.Run();
    else if (args is ["--stripe-demo"]) StripeDemo.Run();
    else if (args is ["--edges-native-test"]) EdgeChecks.Run();
    else if (args is ["--edges-demo"]) EdgeDemo.Run();
    else if (args is ["--shapes-native-test"]) ShapeChecks.Run();
    else if (args is ["--shapes-demo"]) ShapeDemo.Run();
    else if (args is ["--regions-native-test"]) RegionChecks.Run();
    else if (args is ["--regions-demo"]) RegionDemo.Run();
    else if (args is ["--segmentation-native-test"]) SegmentationChecks.Run();
    else if (args is ["--segmentation-demo"]) SegmentationDemo.Run();
    else MatBasics.Run();
    return 0;
}
catch (Exception error)
{
    Exception cause = error;
    while (cause is TypeInitializationException && cause.InnerException is { } inner) cause = inner;
    Console.Error.WriteLine($"Native execution not passed: {cause.GetType().Name}. Verify project-local runtime/architecture and vendor runtime dependencies; no fallback or system changes attempted."); return 3;
}
