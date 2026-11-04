using IndustrialStationIntegrated;
try
{
    if (args is ["--self-test"]) { await IntegratedChecks.RunAsync(); return 0; }
    if (args is ["--demo"]) { await IntegratedChecks.DemoAsync(); return 0; }
    Console.Error.WriteLine("Usage: --self-test | --demo. In-memory motion and owned Mono8 replay only; no actual hardware/native SDK."); return 2;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
