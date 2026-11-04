using IndustrialStationMotion;
try
{
    if (args is ["--self-test"]) { await MotionIntegrationChecks.RunAsync(); return 0; }
    if (args is ["--demo"]) { await MotionIntegrationChecks.DemoAsync(); return 0; }
    Console.Error.WriteLine("Usage: --self-test | --demo. In-memory axis only; no network/native SDK/card endpoint."); return 2;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
