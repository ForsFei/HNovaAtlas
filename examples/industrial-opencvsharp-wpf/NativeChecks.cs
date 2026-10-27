using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IndustrialOpenCvSharp;
using IndustrialVision;
using OpenCvSharp;
using WpfRect = System.Windows.Rect;

namespace IndustrialOpenCvSharpWpf;

public static class NativeChecks
{
    private static RenderTargetBitmap Render(MeasurementViewer viewer, int width = 500, int height = 300)
    {
        viewer.Measure(new(width, height)); viewer.Arrange(new WpfRect(0, 0, width, height)); viewer.UpdateLayout();
        RenderTargetBitmap target = new(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(viewer); target.Freeze(); return target;
    }
    private static (byte R, byte G, byte B) Pixel(BitmapSource bitmap, int x, int y)
    {
        byte[] pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[2], pixel[1], pixel[0]);
    }
    public static async Task Run()
    {
        int count = 0;
        void Check(bool v, string label) { if (!v) throw new InvalidOperationException(label); Console.WriteLine($"PASS {++count}: {label}"); }
        void Reject(Action action, string label) { try { action(); } catch (ArgumentException) { Check(true, label); return; } throw new InvalidOperationException(label); }
        FrameSelection frame = FrameFactory.Create(0);
        RenderedMeasurement output = await Task.Run(() => NativeFrameRunner.Analyze(frame));
        Check(output.Bitmap.IsFrozen && output.Bitmap.Format == PixelFormats.Gray8, "Actual Mat-to-WPF bitmap frozen on worker");
        byte[] copied = new byte[128 * 96]; output.Bitmap.CopyPixels(copied, 128, 0);
        Check(copied.SequenceEqual(frame.Image.CopyPixels()), "Bitmap remains valid after actual worker Mat disposed");
        Check(output.Report is { Outcome: StripeOutcome.WidthMeasured, WidthPixels: { } width } && Math.Abs(width - 32) < 1e-8, "Actual OpenCV Reduce/Gaussian and C# pairing yield32px");
        FrameSession session = new(NativeFrameRunner.Analyze); session.Load(frame);
        MeasurementViewer viewer = new(); viewer.Present(session.View);
        RenderTargetBitmap before = Render(viewer);
        Check(Pixel(before, 10, 150) == (0, 0, 0) && Pixel(before, 480, 150) == (0, 0, 0), "Offscreen render shows both black letterboxes");
        Check(Pixel(before, 200, 150) == (220, 220, 220), "Unmeasured actual image has no orange line");
        var cyan = Pixel(before, 100, 100); Check(cyan.R < 10 && cyan.G > 240 && cyan.B > 240, "Cyan preset ROI drawn at mapped area boundary");
        session.TryRun(out Task pending); await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Check(session.View.Report?.Outcome == StripeOutcome.WidthMeasured && session.View.Error is null, "Real backend result passes session validation");
        viewer.Present(session.View); RenderTargetBitmap measured = Render(viewer);
        var orange = Pixel(measured, 200, 150); Check(orange.R > 240 && orange.G is > 140 and < 190 && orange.B < 10, "Actual matching report draws orange span at y150");
        var endpoint = Pixel(measured, 175, 150); Check(endpoint.R > 240 && endpoint.B < 10, "Actual endpoint marker at half-pixel-correct x175");
        string folder = Path.Combine(AppContext.BaseDirectory, "verification"); Directory.CreateDirectory(folder);
        string artifact = Path.Combine(folder, $"opencv-width-{Guid.NewGuid():N}.png");
        PngBitmapEncoder encoder = new(); encoder.Frames.Add(BitmapFrame.Create(measured));
        using (FileStream file = new(artifact, FileMode.CreateNew)) encoder.Save(file);
        Console.WriteLine($"Offscreen artifact: {artifact}");
        session.Clear(); viewer.Present(session.View); RenderTargetBitmap cleared = Render(viewer);
        Check(Pixel(cleared, 200, 150) == (220, 220, 220), "Clear removes measured line but preserves image");
        session.Load(FrameFactory.Create(1)); viewer.Present(session.View);
        Check(Pixel(Render(viewer), 200, 150) == (20, 20, 20), "Next dark frame has no stale measurement line");
        session.Load(FrameFactory.Create(2)); session.TryRun(out Task many); await many.WaitAsync(TimeSpan.FromSeconds(10));
        viewer.Present(session.View);
        Check(session.View.Report is { Outcome: StripeOutcome.Ambiguous, WidthPixels: null } && Pixel(Render(viewer), 200, 150) == (220, 220, 220), "Actual ambiguous result publishes diagnostics but no orange width");
        session.Load(FrameFactory.Create(3)); session.TryRun(out Task blank); await blank.WaitAsync(TimeSpan.FromSeconds(10));
        Check(session.View.Report is { Outcome: StripeOutcome.NoEdges, WidthPixels: null }, "Actual blank image is NoEdges, not native failure or width0");
        session.Load(frame with { Recipe = frame.Recipe with { Reverse = true } }); session.TryRun(out Task reverse); await reverse.WaitAsync(TimeSpan.FromSeconds(10));
        Check(session.View.Report is { FirstImagePoint: { X: 71.5 }, SecondImagePoint: { X: 39.5 } } && session.View.Error is null, "Actual reversed report revalidated without double-reversing coordinates");
        using (Mat parent = new(4, 8, MatType.CV_8UC1))
        {
            for (int y = 0; y < 4; y++) for (int x = 0; x < 8; x++) parent.Set(y, x, (byte)(y * 10 + x));
            using Mat roi = new(parent, new OpenCvSharp.Rect(3, 1, 2, 2));
            Check(!roi.IsContinuous() && roi.Step() == 8, "Bridge test uses actual noncontinuous ROI with parent stride8");
            BitmapSource roiBitmap = BitmapBridge.FromMat(roi); byte[] p = new byte[4];
            roi.Set(0, 0, (byte)255); roiBitmap.CopyPixels(p, 2, 0);
            Check(p.SequenceEqual(new byte[] { 13, 14, 23, 24 }), "Row-wise bitmap copy excludes padding and is independent of ROI writes");
        }
        using Mat color = new(8, 8, MatType.CV_8UC3);
        Reject(() => BitmapBridge.FromMat(color), "Color Mat not silently treated as Gray8");
        using Mat empty = new(); Reject(() => BitmapBridge.FromMat(empty), "Empty Mat bridge refused");
        Mat? owned = null;
        _ = NativeFrameRunner.WithOwnedMat(frame.Image, mat => { owned = mat; return mat.Type(); });
        Check(owned!.IsDisposed, "Owned Mat disposed after successful callback");
        try { NativeFrameRunner.WithOwnedMat<int>(frame.Image, mat => { owned = mat; throw new ArgumentException("Synthetic processing failure"); }); }
        catch (ArgumentException) { }
        Check(owned!.IsDisposed, "Owned Mat disposed on callback exception");
        using ManualResetEventSlim entered = new(false), release = new(false);
        FrameSession delayed = new(source => { entered.Set(); if (!release.Wait(5000)) throw new TimeoutException(); return NativeFrameRunner.Analyze(source); });
        delayed.Load(frame); delayed.TryRun(out Task old);
        try { Check(entered.Wait(2000) && delayed.Load(FrameFactory.Create(1)), "Real backend handoff held until after display replacement"); }
        finally { release.Set(); }
        await old.WaitAsync(TimeSpan.FromSeconds(10));
        Check(delayed.View.Selected?.Name == "暗槽" && delayed.View.Report is null && delayed.View.Error is null, "Actual late native result cannot overwrite newer display");
        using ManualResetEventSlim entered2 = new(false), release2 = new(false); Mat? inFlight = null;
        FrameSession closing = new(source => NativeFrameRunner.WithOwnedMat(source.Image, mat =>
        {
            inFlight = mat; entered2.Set(); if (!release2.Wait(5000)) throw new TimeoutException();
            return new RenderedMeasurement(BitmapBridge.FromMat(mat), StripeDemo.Measure(mat, source.Recipe));
        }));
        closing.Load(frame); closing.TryRun(out Task last);
        Check(entered2.Wait(2000), "Actual owned Mat held inside in-flight callback"); Task drain = closing.CloseAsync();
        try { Check(!drain.IsCompleted && !inFlight!.IsDisposed, "Close does not dispose Mat still used by worker"); }
        finally { release2.Set(); }
        await drain.WaitAsync(TimeSpan.FromSeconds(10));
        Check(inFlight!.IsDisposed && last.IsCompleted && closing.View.Report is null && closing.View.Bitmap is null, "Drain waits for actual Mat release and suppresses closing publication");
        await session.CloseAsync(); viewer.Present(session.View);
        Check(Pixel(Render(viewer), 200, 150) == (0, 0, 0), "Closed display clears bitmap and graphics together");
        Console.WriteLine($"Native/WPF checks passed: {count}. Actual OpenCV/BitmapSource/offscreen render and Mat disposal; no visible-window, DPI matrix, camera or production validation.");
    }
}
