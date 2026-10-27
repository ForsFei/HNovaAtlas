using IndustrialHandshake;

if (args.Length != 1 || args[0] is not ("--self-test" or "--demo"))
{
    Console.WriteLine("Use --self-test or --demo. Local simulated action only; no hardware endpoint option.");
    return 2;
}
try { await SelfChecks.RunAsync(); return 0; }
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
