using IndustrialStationCommissioning;
try
{
    if (args is ["--self-test"]) { CommissioningChecks.Run(); return 0; }
    if (args is ["--demo"]) { CommissioningChecks.Demo(); return 0; }
    Console.Error.WriteLine("Usage: --self-test | --demo. Offline drills and record template only; no hardware acceptance or machine permission."); return 2;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
