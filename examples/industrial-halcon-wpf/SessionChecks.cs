using System.Windows.Media;
using System.Windows.Media.Imaging;
using IndustrialHalcon;
using IndustrialVision;

namespace IndustrialHalconWpf;

public static class SessionChecks
{
    public static async Task Run()
    {
        int count = 0;
        void Check(bool value, string label)
        {
            if (!value) throw new InvalidOperationException(label);
            Console.WriteLine($"PASS {++count}: {label}");
        }
        Mono8Image frame = PlainPgm.LoadFixture();
        BitmapSource bitmap = BitmapSource.Create(8, 6, 96, 96, PixelFormats.Gray8, null, frame.CopyPixels(), 8);
        bitmap.Freeze(); byte[] readback = new byte[48]; bitmap.CopyPixels(readback, 8, 0);
        Check(bitmap.IsFrozen && bitmap.Format == PixelFormats.Gray8, "Actual WPF Gray8 bitmap is frozen");
        Check(readback.SequenceEqual(frame.CopyPixels()), "Actual WPF pixel readback matches fixture, no native HALCON");
        BlobReport Sample() => BlobReadback.FromArrays(8, 6, 1, 2, 10, [4], [1.5], [1.5]);
        FrameSession empty = new(_ => Sample());
        Check(!empty.TryRun(out _), "No frame means no submission");
        Check(empty.Load(frame) && empty.View.Revision == 1, "Loading increments local display revision");
        Check(empty.TryRun(out Task normal), "First call admitted"); await normal.WaitAsync(TimeSpan.FromSeconds(5));
        Check(empty.View.Report?.Candidates.Single().AreaPixels == 4 && !empty.View.Busy, "Completed managed test-double result publishes");
        Check(empty.Clear() && empty.View.Report is null && empty.View.Revision == 2, "Clear invalidates prior result intent");
        int uiThread = Environment.CurrentManagedThreadId, workerThread = uiThread;
        using ManualResetEventSlim entered = new(false), release = new(false);
        FrameSession slow = new(image => { workerThread = Environment.CurrentManagedThreadId; entered.Set(); if (!release.Wait(5000)) throw new TimeoutException(); return Sample(); });
        slow.Load(frame);
        Check(slow.TryRun(out Task running) && entered.Wait(2000), "Gate-controlled worker entered");
        try
        {
            Check(workerThread != uiThread, "Synchronous backend runs away from submitting thread");
            Check(slow.View.Busy && !slow.TryRun(out _), "One in-flight call, no queued second request");
            Check(slow.Load(Mono8Image.FromPacked(8, 6, new byte[48])), "Display can change during work");
            Check(slow.View.Report is null, "Changed display clears old evidence immediately");
        }
        finally { release.Set(); }
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        Check(slow.View.Report is null && slow.View.Error is null, "Late success cannot overwrite changed frame");
        using ManualResetEventSlim entered2 = new(false), release2 = new(false);
        FrameSession lateFailure = new(_ => { entered2.Set(); if (!release2.Wait(5000)) throw new TimeoutException(); throw new InvalidOperationException("Test failure"); });
        lateFailure.Load(frame); lateFailure.TryRun(out Task failing);
        Check(entered2.Wait(2000) && lateFailure.Clear(), "Clear while backend is executing");
        release2.Set(); await failing.WaitAsync(TimeSpan.FromSeconds(5));
        Check(lateFailure.View.Error is null && lateFailure.View.Report is null, "Late failure also cannot overwrite cleared intent");
        FrameSession broken = new(_ => throw new DllNotFoundException("Synthetic test only")); broken.Load(frame); broken.TryRun(out Task failed);
        await failed.WaitAsync(TimeSpan.FromSeconds(5));
        Check(broken.View.Error == nameof(DllNotFoundException) && broken.View.Report is null, "Synthetic DLL failure stays failure, not NoCandidates or fallback");
        bool idle = !broken.View.Busy;
        Check(broken.TryRun(out Task retry) && idle, "Explicit next request is possible after completed failure");
        await retry.WaitAsync(TimeSpan.FromSeconds(5));
        FrameSession invalid = new(_ => new(BlobOutcome.Candidates, 8, 6, [])); invalid.Load(frame); invalid.TryRun(out Task bad);
        await bad.WaitAsync(TimeSpan.FromSeconds(5));
        Check(invalid.View.Error == nameof(ArgumentException) && invalid.View.Report is null, "Malformed backend outcome rejected");
        FrameSession wrongSize = new(_ => new(BlobOutcome.NoCandidates, 9, 6, [])); wrongSize.Load(frame); wrongSize.TryRun(out Task size);
        await size.WaitAsync(TimeSpan.FromSeconds(5));
        Check(wrongSize.View.Error == nameof(ArgumentException), "Wrong output coordinate envelope rejected");
        using ManualResetEventSlim entered3 = new(false), release3 = new(false);
        FrameSession closing = new(_ => { entered3.Set(); if (!release3.Wait(5000)) throw new TimeoutException(); return Sample(); });
        closing.Load(frame); closing.TryRun(out Task last);
        Check(entered3.Wait(2000), "Close test holds an actual in-flight managed call");
        Task drain = closing.CloseAsync();
        try
        {
            Check(!drain.IsCompleted && closing.View.Closing, "Close waits; it does not claim to cancel native execution");
            Check(!closing.TryRun(out _) && !closing.Load(frame) && !closing.Clear(), "Closing rejects all new intent");
            Check(ReferenceEquals(drain, closing.CloseAsync()), "Repeated close observes the same drain task");
        }
        finally { release3.Set(); }
        await drain.WaitAsync(TimeSpan.FromSeconds(5));
        Check(last.IsCompleted && closing.View.Report is null && !closing.View.Busy, "Drained call cannot publish after closing");
        await empty.CloseAsync(); Check(empty.View.Closing, "Idle close also completes");
        bool refused = false;
        try { new FrameSession(_ => Sample()).Load(Mono8Image.FromPacked(1, 1, [0])); } catch (ArgumentException) { refused = true; }
        Check(refused, "Nonfixture frame size refused before native handoff");
        Console.WriteLine($"WPF/session checks passed: {count}. Managed test doubles plus actual BitmapSource only; no native HALCON, visible-window, camera or production validation.");
    }
}
