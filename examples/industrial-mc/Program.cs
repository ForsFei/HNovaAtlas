using IndustrialMc;

if (args.Length != 1 || args[0] is not ("--self-test" or "--demo"))
{
    Console.WriteLine("Use --self-test or --demo. Own 127.0.0.1 teaching server only.");
    return 2;
}
try
{
    await SelfChecks.RunAsync();
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}
