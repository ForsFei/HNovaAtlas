using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace IndustrialVision.Wpf;

public static class WpfChecks
{
    private static int count;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine($"PASS {++count}: {label}");
    }
    private static RenderTargetBitmap Render(Mono8Viewer viewer, int width, int height)
    {
        viewer.Measure(new(width, height)); viewer.Arrange(new(0, 0, width, height)); viewer.UpdateLayout();
        RenderTargetBitmap target = new(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(viewer); target.Freeze(); return target;
    }
    private static (byte R, byte G, byte B) Pixel(BitmapSource image, int x, int y)
    {
        byte[] pixel = new byte[4]; image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[2], pixel[1], pixel[0]);
    }
    private static void Save(BitmapSource image, string name)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "verification"); Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, name);
        if (File.Exists(file)) throw new IOException($"Refusing to overwrite verification artifact: {file}. Run with a fresh output directory.");
        PngBitmapEncoder encoder = new(); encoder.Frames.Add(BitmapFrame.Create(image));
        using FileStream stream = new(file, FileMode.CreateNew); encoder.Save(stream);
        Console.WriteLine($"Rendered: {file}");
    }
    public static async Task Run()
    {
        count = 0;
        Check(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA, "WPF checks run on an STA dispatcher thread");
        Mono8Image image = PlainPgm.LoadFixture();
        BitmapSource bitmap = Mono8Bitmap.Create(image);
        Check(bitmap.IsFrozen && bitmap.PixelWidth == 8 && bitmap.PixelHeight == 6 && bitmap.Format == PixelFormats.Gray8, "Gray8 bitmap has real dimensions and is frozen");
        byte[] actual = new byte[48]; bitmap.CopyPixels(actual, 8, 0);
        Check(actual.AsSpan().SequenceEqual(image.CopyPixels()), "Bitmap pixel copy matches source owned Mono8 bytes");
        BitmapSource fromWorker = await Task.Run(() => Mono8Bitmap.Create(image));
        Check(fromWorker.IsFrozen && fromWorker.PixelWidth == 8, "Frozen worker-created bitmap can be read on UI thread");
        bool dispatcherReturned = false;
        // The real UI dispatcher must be captured before entering a worker.
        Dispatcher ui = Application.Current.Dispatcher;
        await Task.Run(async () => await ui.InvokeAsync(() => dispatcherReturned = ui.CheckAccess()));
        Check(dispatcherReturned, "Worker-to-UI InvokeAsync is awaited and executes on UI dispatcher");

        Guid owner = Guid.NewGuid(), camera = Guid.NewGuid();
        ProcessingRecipe recipe = new("roi-statistics-v1", 1, new(1, 1, 2, 2), 200);
        using OfflineFrameSource source = new([image]); source.Open();
        CaptureResult Capture(uint number)
        {
            using FrameLease lease = source.Acquire();
            return new(new(new(owner, number), $"render-{number}", 1), new(lease.CopyToOwned(), camera, 1, number, 0, true, true));
        }
        CaptureResult a = Capture(1), b = Capture(2);
        InspectionPipeline pipeline = new(owner, 8, 6, recipe);
        try
        {
            FrameDisplayState state = new(owner, pipeline.Session, recipe);
            Mono8Viewer viewer = new();
            Check(state.Show(a), "Actual viewer accepts an owned offline display frame");
            viewer.Present(state);
            RenderTargetBitmap before = Render(viewer, 500, 300);
            Check(Pixel(before, 10, 150) == (0, 0, 0) && Pixel(before, 475, 150) == (0, 0, 0), "Offscreen render has black side letterboxes");
            Check(Pixel(before, 125, 75) == (220, 220, 220), "Offscreen render samples correct bright fixture pixel");
            Check(viewer.TryPick(new(125, 75), out PixelHit hit, out byte value) && hit == new PixelHit(1, 1) && value == 220, "Viewer click conversion selects original pixel and value");
            Check(!viewer.TryPick(new(10, 150), out _, out _), "Viewer refuses clicks on letterbox");
            InspectionResult result = await pipeline.TrySubmit(a).Result!;
            Check(state.TryAttach(result), "Real pipeline result attaches to displayed frame before rendering ROI");
            viewer.Present(state);
            RenderTargetBitmap annotated = Render(viewer, 500, 300);
            (byte r, byte g, byte blue) = Pixel(annotated, 100, 100);
            Check(r < 10 && g > 240 && blue > 240, "Matching result produces actual cyan ROI stroke at expected pixel edge");
            _ = state.Show(b); Check(!state.TryAttach(result), "Old result rejected after next display frame");
            viewer.Present(state);
            RenderTargetBitmap changed = Render(viewer, 500, 300);
            Check(Pixel(changed, 100, 100) == (220, 220, 220), "Switching displayed frame actually removes stale ROI stroke");
            state.Clear(); viewer.Present(state);
            RenderTargetBitmap cleared = Render(viewer, 500, 300);
            Check(Pixel(cleared, 125, 75) == (0, 0, 0) && !viewer.TryPick(new(125, 75), out _, out _), "Clear removes bitmap and hit-test source together");
            Check(pipeline.Acknowledge(result) && pipeline.RetainedCount == 0, "Display rejection does not leak pipeline result capacity");
            // Generated files go only under the ignored build-output folder, with collision-safe names.
            string run = Guid.NewGuid().ToString("N");
            Save(before, $"{run}-plain.png"); Save(annotated, $"{run}-matched.png"); Save(changed, $"{run}-changed.png");
        }
        finally { pipeline.Complete(); await pipeline.Completion; }
        MainWindow window = new(shutdownOnClose: false); // Never Show(): XAML/events/lifecycle only, no desktop manipulation.
        try
        {
            Button nextButton = (Button)window.FindName("NextButton"), processButton = (Button)window.FindName("ProcessButton"), clearButton = (Button)window.FindName("ClearButton");
            TextBlock frameText = (TextBlock)window.FindName("FrameText"), resultText = (TextBlock)window.FindName("ResultText"), statusText = (TextBlock)window.FindName("StatusText");
            Check(frameText.Text.Contains("offline-1") && resultText.Text.Contains("无已匹配"), "Real XAML window initializes with image identity and no fabricated measurement");
            processButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!processButton.IsEnabled && nextButton.IsEnabled, "Actual process event disables reentry but keeps preview switching available");
            nextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.WhenProcessingIdle.WaitAsync(TimeSpan.FromSeconds(5));
            Check(frameText.Text.Contains("offline-2") && resultText.Text.Contains("无已匹配") && statusText.Text.Contains("未叠加"), "Actual delayed UI continuation refuses old frame overlay");
            processButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.WhenProcessingIdle.WaitAsync(TimeSpan.FromSeconds(5));
            Check(resultText.Text.Contains(35d.ToString("F2")) && !processButton.IsEnabled, "Actual inverted second fixture produces mean 35 and blocks repeat processing");
            FrameworkElement content = (FrameworkElement)window.Content;
            content.Measure(new(900, 680)); content.Arrange(new(0, 0, 900, 680)); content.UpdateLayout();
            DrawingVisual background = new();
            using (DrawingContext drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, 900, 680));
            RenderTargetBitmap full = new(900, 680, 96, 96, PixelFormats.Pbgra32);
            full.Render(background); full.Render(content); full.Freeze();
            Save(full, $"{Guid.NewGuid():N}-window-content.png");
            clearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(frameText.Text.Contains("无图像") && !processButton.IsEnabled, "Actual clear event removes frame and disables processing without a source");
            nextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            processButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Close();
            await window.WhenClosed.WaitAsync(TimeSpan.FromSeconds(5));
            Check(window.WhenProcessingIdle.IsCompleted && !processButton.IsEnabled, "Actual close event awaits accepted processing/UI handoff before finishing");
        }
        finally
        {
            if (!window.WhenClosed.IsCompleted) { window.Close(); await window.WhenClosed.WaitAsync(TimeSpan.FromSeconds(5)); }
        }
        Console.WriteLine($"WPF offscreen checks passed: {count}. No visible window, hardware or manual UI acceptance.");
    }
}
