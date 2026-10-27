using IndustrialRobotics;

if (args.Length != 1 || args[0] is not ("--self-test" or "--demo" or "--task-test" or "--task-demo"))
{
    Console.WriteLine("Use --self-test, --demo, --task-test or --task-demo. Offline only; no robot SDK or network.");
    return 2;
}
try
{
    if (args[0] == "--self-test") RobotChecks.Run();
    else if (args[0] == "--task-test") RobotTaskChecks.Run();
    else if (args[0] == "--task-demo") RobotTaskChecks.Demo();
    else RobotChecks.Demo();
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
