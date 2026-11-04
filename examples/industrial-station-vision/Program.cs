using IndustrialStationVision;
try
{
    if (args is ["--self-test"]) { await VisionIntegrationChecks.RunAsync(); return 0; }
    if (args is ["--demo"]) { await VisionIntegrationChecks.DemoAsync(); return 0; }
    Console.Error.WriteLine("Usage: --self-test | --demo. Owned Mono8 replay only; no camera/native SDK/model/hardware endpoint."); return 2;
}
catch (Exception failure) { Console.Error.WriteLine(failure); return 1; }
