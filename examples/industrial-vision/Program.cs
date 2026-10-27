using IndustrialVision;

if (args.Length != 1 || args[0] is not ("--self-test" or "--demo" or "--capture-test" or "--capture-demo" or "--pipeline-test" or "--pipeline-demo" or "--calibration-test" or "--calibration-demo" or "--display-test"))
{
    Console.WriteLine("Use --self-test, --demo, --capture-test, --capture-demo, --pipeline-test, --pipeline-demo, --calibration-test, --calibration-demo or --display-test. Offline only; no camera or SDK.");
    return 2;
}
try
{
    if (args[0] == "--self-test") VisionChecks.Run();
    else if (args[0] == "--capture-test") CaptureChecks.Run();
    else if (args[0] == "--capture-demo") CaptureChecks.Demo();
    else if (args[0] == "--pipeline-test") await PipelineChecks.Run();
    else if (args[0] == "--pipeline-demo") await PipelineChecks.Demo();
    else if (args[0] == "--calibration-test") CalibrationChecks.Run();
    else if (args[0] == "--calibration-demo") CalibrationChecks.Demo();
    else if (args[0] == "--display-test") await DisplayChecks.Run();
    else VisionChecks.Demo();
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
