using IndustrialStationHandover;
try
{
    if (args is ["--self-test"]) { HandoverChecks.Run(); return 0; }
    if (args is ["--demo"]) { HandoverChecks.Demo(); return 0; }
    Console.Error.WriteLine("Usage: --self-test | --demo. Offline handover material only; no hardware installation or application deployment."); return 2;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
