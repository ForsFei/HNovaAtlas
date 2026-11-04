using System.Windows;

namespace IndustrialYolo.Wpf;

public static class App
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--open", string model]) return new Application().Run(new MainWindow(model, null));
        if (args is ["--open", string model2, string image]) return new Application().Run(new MainWindow(model2, image));
        if (args is ["--open-reused", string reused]) return new Application().Run(new MainWindow(reused, null, reuseSession: true));
        if (args is not ["--self-test"] and not ["--native-test", _] and not ["--pipeline-test"] and not ["--pipeline-native-test", _])
        { Console.Error.WriteLine("Usage: --open <model> [image] | --open-reused <model> | --self-test | --native-test <model> | --pipeline-test | --pipeline-native-test <model>"); return 2; }
        Application app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try
            {
                if (args[0] == "--self-test") await ViewerChecks.Managed();
                else if (args[0] == "--pipeline-test") await WorkerChecks.Managed();
                else if (args[0] == "--pipeline-native-test") await WorkerChecks.Native(args[1]);
                else await ViewerChecks.Native(args[1]);
                app.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine(error); app.Shutdown(args[0] is "--self-test" or "--pipeline-test" ? 1 : 3); }
        };
        return app.Run();
    }
}
