using System.Windows;

namespace IndustrialOpenCvSharpWpf;

public static class App
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 0) return new Application().Run(new MainWindow());
        if (args is not ["--self-test"] and not ["--native-test"] and not ["--pipeline-test"] and not ["--pipeline-native-test"] and not ["--pipeline-demo"]) return 2;
        bool native = args[0] is "--native-test" or "--pipeline-native-test" or "--pipeline-demo";
        Application app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try
            {
                if (args[0] == "--pipeline-test") await PipelineChecks.Run();
                else if (args[0] == "--pipeline-native-test") await PipelineNativeChecks.Run();
                else if (args[0] == "--pipeline-demo") await PipelineDemo.Run();
                else if (native) await NativeChecks.Run();
                else await SessionChecks.Run();
                app.Shutdown(0);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(native ? 3 : 1); }
        };
        return app.Run();
    }
}
