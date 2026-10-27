using IndustrialLeadshineDiagnostics;

if (args.Length != 1 || args[0] is not ("--self-test" or "--demo"))
{
    Console.WriteLine("Use --self-test or --demo. TEST DOUBLE ONLY; no native backend or hardware mode.");
    return 2;
}
try
{
    if (args[0] == "--self-test") SelfChecks.Run();
    else
    {
        var api = new FakeReadOnlyDmc();
        using var probe = new CardProbe(api, FakeReadOnlyDmc.Configuration);
        probe.Open();
        ProbeSnapshot sample = probe.Sample();
        Console.WriteLine($"TEST DOUBLE: card={sample.Card.Id}, axis={sample.Axis}, node={sample.Node}, state={sample.DriveState}");
        Console.WriteLine($"command={sample.CommandPosition} unit, speed={sample.CommandSpeed} unit/s, fresh={probe.IsCurrent(sample, TimeSpan.FromSeconds(2))}");
        api.Alarm = 17; api.Drive = 7;
        sample = probe.Sample();
        Console.WriteLine($"Fault remains observable: state={sample.DriveState}, error={sample.AxisError}; no reset/enable command exists.");
        probe.Close();
        Console.WriteLine($"After close fresh={probe.IsCurrent(sample, TimeSpan.FromSeconds(2))}. No DLL/driver/card accessed.");
    }
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
