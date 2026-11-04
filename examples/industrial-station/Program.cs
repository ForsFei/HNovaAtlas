using IndustrialStation;

try
{
    if (args is ["--self-test"]) { StationChecks.Run(); return 0; }
    if (args is ["--demo"]) { OfflineSimulation.Demo(); return 0; }
    if (args is ["--host-test"]) { await HostChecks.RunAsync(); return 0; }
    if (args is ["--async-demo"]) { await AsyncSimulation.DemoAsync(); return 0; }
    if (args is ["--ledger-test"]) { LedgerChecks.Run(); return 0; }
    if (args is ["--ledger-demo"]) { LedgerSimulation.Demo(); return 0; }
    if (args is ["--application-test"]) { await ApplicationChecks.RunAsync(); return 0; }
    if (args is ["--application-demo"]) { await ApplicationChecks.DemoAsync(); return 0; }
    Console.Error.WriteLine("Usage: --self-test | --demo | --host-test | --async-demo | --ledger-test | --ledger-demo | --application-test | --application-demo. Offline declarations only; no hardware endpoint option."); return 2;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
