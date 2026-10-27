using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IndustrialOpenCvSharp;
using IndustrialVision;

namespace IndustrialOpenCvSharpWpf;

public static class SessionChecks
{
    // Explicit managed test double: projection without native Gaussian; NEVER used by the actual UI.
    internal static RenderedMeasurement Fake(FrameSelection source)
    {
        StripeRecipe r = source.Recipe;
        double[] p = new double[r.Length]; bool x = r.Axis == ScanAxis.X;
        int across = x ? r.Window.Height : r.Window.Width;
        for (int i = 0; i < p.Length; i++)
            for (int j = 0; j < across; j++)
                p[i] += source.Image.GetPixel(r.Window.X + (x ? i : j), r.Window.Y + (x ? j : i)) / (double)across;
        return new(BitmapBridge.FromOwned(source.Image), StripeAnalysis.Analyze(p, p, r));
    }
    public static async Task Run()
    {
        int count = 0;
        void Check(bool v, string label) { if (!v) throw new InvalidOperationException(label); Console.WriteLine($"PASS {++count}: {label}"); }
        void Reject(Action action, string label) { try { action(); } catch (ArgumentException) { Check(true, label); return; } throw new InvalidOperationException(label); }
        Check(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA, "Actual WPF checks use STA dispatcher");
        FrameSelection bright = FrameFactory.Create(0), dark = FrameFactory.Create(1);
        BitmapSource bitmap = BitmapBridge.FromOwned(bright.Image);
        byte[] pixels = new byte[128 * 96]; bitmap.CopyPixels(pixels, 128, 0);
        Check(bitmap.IsFrozen && bitmap.Format == PixelFormats.Gray8 && bitmap.PixelWidth == 128 && bitmap.PixelHeight == 96, "Actual Gray8 frozen bitmap and dimensions");
        Check(pixels.SequenceEqual(bright.Image.CopyPixels()), "WPF bitmap readback equals owned pixels");
        BitmapSource workerBitmap = await Task.Run(() => BitmapBridge.FromOwned(dark.Image));
        Check(workerBitmap.IsFrozen && workerBitmap.PixelWidth == 128, "Frozen worker bitmap readable from UI dispatcher");
        var dispatcher = Application.Current.Dispatcher; bool dispatched = false;
        await Task.Run(async () => await dispatcher.InvokeAsync(() => dispatched = dispatcher.CheckAccess()));
        Check(dispatched, "Awaited InvokeAsync executes on captured UI dispatcher");
        ImageViewport v = ImageViewport.Create(128, 96, 500, 300)!;
        Check(v.Scale == 3.125 && v.Bounds == new Rect(50, 0, 400, 300), "Uniform viewport has correct horizontal letterboxes");
        Check(v.PixelCenter(new(39.5, 47.5)) == new Point(175, 150), "Pixel-center and edge coordinate mapping share half-pixel convention");
        Check(v.Window(new(16, 16, 96, 64)) == new Rect(100, 50, 300, 200), "ROI area edge mapped without extra half pixel");
        Check(ImageViewport.Create(128, 96, 400, 400)!.Bounds == new Rect(0, 50, 400, 300), "Vertical letterboxes after resizing");
        Check(ImageViewport.Create(0, 96, 500, 300) is null && ImageViewport.Create(128, 96, double.NaN, 300) is null && ImageViewport.Create(128, 96, double.Epsilon, 300) is null,
            "Invalid viewport and underflow refused");
        FrameSession session = new(Fake);
        Check(!session.TryRun(out _), "No image means no processing request");
        Check(session.Load(bright) && session.View.Revision == 1, "Load establishes local revision");
        Check(session.TryRun(out Task first), "First managed test-double request admitted"); await first.WaitAsync(TimeSpan.FromSeconds(5));
        Check(session.View.Report?.WidthPixels == 32 && session.View.Error is null && !session.View.Busy, "Managed test-double result publishes consistently");
        Check(session.Clear() && session.View.Revision == 2 && session.View.Report is null && session.View.Bitmap is not null, "Clear invalidates result while preserving image");
        int uiThread = Environment.CurrentManagedThreadId, workerThread = uiThread;
        using ManualResetEventSlim entered = new(false), release = new(false);
        FrameSession slow = new(source => { workerThread = Environment.CurrentManagedThreadId; entered.Set(); if (!release.Wait(5000)) throw new TimeoutException(); return Fake(source); });
        slow.Load(bright); Check(slow.TryRun(out Task running) && entered.Wait(2000), "Deterministic worker gate entered");
        try
        {
            Check(uiThread != workerThread, "Backend runs off UI thread");
            Check(slow.View.Busy && !slow.TryRun(out _), "Busy refuses second request; no queue");
            Check(slow.Load(dark) && slow.View.Report is null, "Image replacement immediately invalidates old result");
        }
        finally { release.Set(); }
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        Check(slow.View.Selected == dark && slow.View.Report is null && slow.View.Error is null, "Late success cannot overwrite changed image");
        using ManualResetEventSlim entered2 = new(false), release2 = new(false);
        FrameSession lateFailure = new(_ => { entered2.Set(); if (!release2.Wait(5000)) throw new TimeoutException(); throw new InvalidOperationException("Synthetic failure"); });
        lateFailure.Load(bright); lateFailure.TryRun(out Task failing);
        try { Check(entered2.Wait(2000) && lateFailure.Clear(), "Clear changes revision while worker is held"); }
        finally { release2.Set(); }
        await failing.WaitAsync(TimeSpan.FromSeconds(5));
        Check(lateFailure.View.Error is null && lateFailure.View.Report is null, "Late error also discarded after clear");
        FrameSession broken = new(_ => throw new DllNotFoundException("Synthetic only"));
        broken.Load(bright); broken.TryRun(out Task failure); await failure.WaitAsync(TimeSpan.FromSeconds(5));
        Check(broken.View.Error == nameof(DllNotFoundException) && broken.View.Report is null, "Backend failure stays failure, not NoEdges or fallback");
        Check(broken.TryRun(out Task retry), "Explicit retry admitted after completed request"); await retry.WaitAsync(TimeSpan.FromSeconds(5));
        FrameSession wrongWidth = new(source => { var o = Fake(source); return o with { Report = o.Report with { WidthPixels = 999 } }; });
        wrongWidth.Load(bright); wrongWidth.TryRun(out Task invalid); await invalid.WaitAsync(TimeSpan.FromSeconds(5));
        Check(wrongWidth.View.Error == nameof(ArgumentException), "Inconsistent measurement metadata refused");
        FrameSession wrongImage = new(source => Fake(source) with { Bitmap = BitmapBridge.FromOwned(dark.Image) });
        wrongImage.Load(bright); wrongImage.TryRun(out Task mismatch); await mismatch.WaitAsync(TimeSpan.FromSeconds(5));
        Check(wrongImage.View.Error == nameof(ArgumentException), "Same-size different bitmap pixels refused");
        FrameSession unfrozen = new(source => Fake(source) with { Bitmap = BitmapSource.Create(128, 96, 96, 96, PixelFormats.Gray8, null, source.Image.CopyPixels(), 128) });
        unfrozen.Load(bright); unfrozen.TryRun(out Task mutable); await mutable.WaitAsync(TimeSpan.FromSeconds(5));
        Check(unfrozen.View.Error == nameof(ArgumentException), "Unfrozen worker bitmap refused before UI publication");
        FrameSelection reversed = bright with { Recipe = bright.Recipe with { Reverse = true } };
        session.Load(reversed); session.TryRun(out Task reverse); await reverse.WaitAsync(TimeSpan.FromSeconds(5));
        Check(session.View.Report is { WidthPixels: 32, FirstImagePoint: { X: 71.5 }, SecondImagePoint: { X: 39.5 } }, "Revalidation correctly handles scan-ordered reverse profiles");
        double[] exposed = []; List<ScanEdge> exposedEdges = [];
        FrameSession detach = new(source =>
        {
            var o = Fake(source); exposed = o.Report.SmoothedProfile.ToArray(); exposedEdges = o.Report.Edges.ToList();
            return o with { Report = o.Report with { SmoothedProfile = exposed, Edges = exposedEdges } };
        });
        detach.Load(bright); detach.TryRun(out Task copied); await copied.WaitAsync(TimeSpan.FromSeconds(5));
        exposed[0] = 255; exposedEdges.Clear();
        Check(detach.View.Report is { Edges.Count: 2 } detached && detached.SmoothedProfile[0] == 20, "Publication detaches backend profile and edge collections");
        using ManualResetEventSlim entered3 = new(false), release3 = new(false);
        FrameSession closing = new(source => { entered3.Set(); if (!release3.Wait(5000)) throw new TimeoutException(); return Fake(source); });
        closing.Load(bright); closing.TryRun(out Task last);
        Check(entered3.Wait(2000), "Closing test has held in-flight work"); Task drain = closing.CloseAsync();
        try
        {
            Check(!drain.IsCompleted && closing.View.Closing && closing.View.Bitmap is null, "Closing clears display but waits for actual task exit");
            Check(!closing.TryRun(out _) && !closing.Load(bright) && !closing.Clear(), "Closing rejects new display and processing intent");
            Check(ReferenceEquals(drain, closing.CloseAsync()), "Repeated close observes same drain task");
        }
        finally { release3.Set(); }
        await drain.WaitAsync(TimeSpan.FromSeconds(5));
        Check(last.IsCompleted && closing.View.Report is null && !closing.View.Busy && closing.View.Selected is null, "Drained work cannot republish or retain closed image");
        await session.CloseAsync(); Check(session.View.Closing, "Idle close completes");
        Reject(() => new FrameSession(Fake).Load(bright with { Recipe = bright.Recipe with { Window = new(100, 0, 96, 64) } }), "Bad ROI rejected before preview processing");
        Console.WriteLine($"WPF/session checks passed: {count}. Actual BitmapSource/dispatcher plus managed test doubles; no OpenCV, visible-window or hardware validation.");
    }
}
