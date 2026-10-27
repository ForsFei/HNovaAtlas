using System.Windows;

namespace IndustrialVision.Wpf;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (e.Args is ["--self-test"])
            {
                await WpfChecks.Run(); Shutdown(0); return;
            }
            if (e.Args.Length != 0) { Console.Error.WriteLine("No arguments for UI; --self-test for offscreen WPF checks."); Shutdown(2); return; }
            MainWindow = new MainWindow(); MainWindow.Show();
        }
        catch (Exception error) { Console.Error.WriteLine(error); Shutdown(1); }
    }
}
