using IndustrialStationCommunication;

try
{
    if (args is ["--self-test"]) { await CommunicationChecks.RunAsync(); return 0; }
    if (args is ["--demo"]) { await CommunicationChecks.DemoAsync(); return 0; }
    Console.Error.WriteLine("Usage: --self-test | --demo. Only 127.0.0.1 random-port MC loopback; no hardware address option."); return 2;
}
catch (Exception failure) { Console.Error.WriteLine(failure); return 1; }
