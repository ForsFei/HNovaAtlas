using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IndustrialStation.Wpf;

internal static class WpfChecks
{
    private static int count;
    public static async Task RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "hnova-station-ui-checks-" + Guid.NewGuid().ToString("N"));
        StationApplication app = await StationApplication.OpenAsync(directory);
        MainWindow window = new(app);
        window.Refresh(); Check(window.StartEnabled && window.StatusText.Contains("Idle"), "idle controls reflect application");
        await app.StartAsync("ui-check", "demo-ng-v1"); window.Refresh();
        Check(!window.StartEnabled && window.StatusText.Contains("demo-ng-v1"), "captured recipe displayed, start disabled");
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        while (app.Snapshot.Host.Station.Terminal is null) await Task.Delay(10, deadline.Token);
        window.Refresh(); Check(!window.StartEnabled && window.StatusText.Contains("Completed"), "terminal does not enable next cycle");
        await app.SaveResultAsync(); await app.SimulateReviewAndReleaseAsync(PeerResultDisposition.AcceptedByPeer);
        window.Refresh(); Check(window.StartEnabled && window.StatusText.Contains("Idle"), "explicit review enables next cycle");
        FrameworkElement content = (FrameworkElement)window.Content;
        ((System.Windows.Controls.ScrollViewer)content).Background = Brushes.White;
        content.Measure(new Size(1000, 760)); content.Arrange(new Rect(0, 0, 1000, 760)); content.UpdateLayout();
        RenderTargetBitmap bitmap = new(1000, 760, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        string screenshot = Path.Combine(directory, "..", Path.GetFileName(directory) + ".png");
        PngBitmapEncoder encoder = new(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream output = File.Create(screenshot)) encoder.Save(output);
        byte[] pixels = new byte[1000 * 760 * 4]; bitmap.CopyPixels(pixels, 1000 * 4, 0);
        Check(pixels.Where((_, i) => i % 4 == 3).Any(alpha => alpha == 255) && pixels.Where((_, i) => i % 4 != 3).Any(color => color > 0), "offscreen WPF contains painted pixels");
        window.Close(); // Closing event cancels first close and requests application shutdown.
        while (!app.Snapshot.Closed) { await window.PumpCloseAsync(); await Task.Delay(1, deadline.Token); }
        Check(app.Snapshot.Host.PortDisposed && app.Snapshot.Closed, "window close pumps host and ledger cleanup");
        using StationLedger reopened = new(directory);
        Check(reopened.Report.CanStartNewCycle && reopened.Report.Cycles.Single().Review is not null, "UI scenario records survive close");
        Console.WriteLine($"PASS {count} WPF offscreen/enablement/close checks; screenshot={Path.GetFullPath(screenshot)}; ledger={directory}");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("WPF check failed: " + message); count++; }
}
