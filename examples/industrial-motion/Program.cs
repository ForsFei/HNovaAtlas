using IndustrialMotion;

if (args.Length != 1 || args[0] is not ("--self-test" or "--demo" or "--observation-test" or "--observation-demo" or "--manual-test" or "--manual-demo" or "--pulse-test" or "--pulse-demo" or "--cycle-test" or "--cycle-demo" or "--geometry-test" or "--geometry-demo" or "--shot-test" or "--shot-demo" or "--recovery-test" or "--recovery-demo" or "--handoff-test" or "--handoff-demo"))
{
    Console.WriteLine("Use --self-test, --demo, --observation-test, --observation-demo, --manual-test, --manual-demo, --pulse-test, --pulse-demo, --cycle-test, --cycle-demo, --geometry-test, --geometry-demo, --shot-test, --shot-demo, --recovery-test, --recovery-demo, --handoff-test or --handoff-demo. No native SDK or real hardware.");
    return 2;
}
try
{
    if (args[0] == "--self-test") SelfChecks.Run();
    else if (args[0] == "--observation-test") ObservationChecks.Run();
    else if (args[0] == "--observation-demo") ObservationChecks.Demo();
    else if (args[0] == "--manual-test") ManualChecks.Run();
    else if (args[0] == "--manual-demo") ManualChecks.Demo();
    else if (args[0] == "--pulse-test") PulseChecks.Run();
    else if (args[0] == "--pulse-demo") PulseChecks.Demo();
    else if (args[0] == "--cycle-test") CycleChecks.Run();
    else if (args[0] == "--cycle-demo") CycleChecks.Demo();
    else if (args[0] == "--geometry-test") GeometryChecks.Run();
    else if (args[0] == "--geometry-demo") GeometryChecks.Demo();
    else if (args[0] == "--shot-test") ShotChecks.Run();
    else if (args[0] == "--shot-demo") ShotChecks.Demo();
    else if (args[0] == "--recovery-test") RecoveryChecks.Run();
    else if (args[0] == "--recovery-demo") RecoveryChecks.Demo();
    else if (args[0] == "--handoff-test") HandoffChecks.Run();
    else if (args[0] == "--handoff-demo") HandoffChecks.Demo();
    else
    {
        using var axis = new SimAxis(); axis.Open(); axis.Enable();
        CommandKey home = axis.Home();
        while (axis.Read().Busy) axis.Tick(.01);
        Console.WriteLine($"Home: {axis.Read().State}, completed={axis.Read().CompletedFor(home)}, machine={axis.Read().CommandPosition:F3} mm");
        CommandKey move = axis.MoveAbsolute(25, 10);
        while (axis.Read().Busy) axis.Tick(.01);
        Console.WriteLine($"Move: {axis.Read().Result}, completed={axis.Read().CompletedFor(move)}, feedback={axis.Read().FeedbackPosition:F3} mm");
        axis.Disable(); axis.Close();
        Console.WriteLine("Simulated only. No driver/DLL/card/servo enable executed.");
    }
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
