using System.IO;
using System.Windows;

namespace IndustrialStation.Wpf;

public static class App
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--open", string directory]) return new Application().Run(new MainWindow(directory));
        if (args is ["--open-demo"])
            return new Application().Run(new MainWindow(Path.Combine(Path.GetTempPath(), "hnova-station-window-" + Guid.NewGuid().ToString("N"))));
        if (args is not ["--ui-test"]) { Console.Error.WriteLine("Usage: --open <dedicated-local-directory> | --open-demo | --ui-test"); return 2; }
        Application app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try { await WpfChecks.RunAsync(); app.Shutdown(0); }
            catch (Exception error) { Console.Error.WriteLine(error); app.Shutdown(1); }
        };
        return app.Run();
    }
}
