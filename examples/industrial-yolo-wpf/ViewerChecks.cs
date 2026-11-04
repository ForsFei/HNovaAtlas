using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IndustrialYolo;
using IndustrialYolo.Images;
using OpenCvSharp;
using Rect = System.Windows.Rect;

namespace IndustrialYolo.Wpf;

internal static class ViewerChecks
{
    private static int count;
    public static async Task Managed()
    {
        count = 0;
        ImageSnapshot a = await Task.Run(() => Snapshot(20, 10));
        ImageSnapshot b = Snapshot(20, 10);
        Check(a.Id != b.Id, "same pixels, distinct image occurrence");
        Check(a.Bitmap.IsFrozen, "worker-created bitmap frozen");
        Check(a.Bitmap.Format == PixelFormats.Bgr24 && a.Bitmap.DpiX == 96, "explicit BGR/96DPI");
        byte[] pixel = new byte[3]; a.Bitmap.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 3, 0);
        Check(pixel.SequenceEqual(new byte[] { 200, 30, 10 }), "UI reads worker BGR without channel swap");
        DisplayMapping wide = DisplayMapping.Create(20, 10, 400, 400)!;
        Check(wide.Scale == 20 && wide.ImageArea == new Rect(0, 100, 400, 200), "centered uniform mapping");
        Check(wide.Map(new(5, 2, 15, 8)) == new Rect(100, 140, 200, 120), "source box and image share mapping");
        Check(DisplayMapping.Create(10, 20, 400, 400)!.ImageArea == new Rect(100, 0, 200, 400), "portrait letterbox");
        Check(DisplayMapping.Create(20, 10, 0, 400) is null, "zero layout skipped");
        Check(DisplayMapping.Create(20, 10, double.NaN, 400) is null, "nonfinite layout skipped");
        Check(DisplayMapping.Create(20, 10, 200, 100)!.Scale == 10, "resize recalculates mapping");
        OverlayResult ar = Hand(a), br = Hand(b);
        Check(ar.Boxes.Count == 1 && ar.Kept == 1, "handwritten overlay fixture only");
        Reject(() => new OverlayResult(a, [new(0, 80, .5f, new(1, 1, 2, 2))], 1), "wrong class");
        Reject(() => new OverlayResult(a, [new(0, 1, float.NaN, new(1, 1, 2, 2))], 1), "NaN score");
        Reject(() => new OverlayResult(a, [new(0, 1, .5f, new(-1, 1, 2, 2))], 1), "unclipped box refused");
        Reject(() => new OverlayResult(a, ar.Boxes.Concat(ar.Boxes), 2), "duplicate rows refused");
        DisplaySession session = new();
        Check(session.TryBegin() is null, "no image no inference");
        session.Show(a); InferenceRequest r = session.TryBegin()!;
        Check(session.Busy && session.TryBegin() is null, "one active inference");
        Check(session.Complete(r with { }, ar) == Attachment.Rejected && session.Busy, "cloned receipt cannot release slot");
        session.Show(b);
        Check(session.Result is null && session.Busy, "switch clears result but preserves slot");
        Check(session.Complete(r, ar) == Attachment.Stale && !session.Busy && session.Current == b, "late old image refused");
        r = session.TryBegin()!;
        Check(session.Complete(r, br) == Attachment.Attached && session.Result == br, "matching result attached");
        Check(session.Complete(r, br) == Attachment.Rejected && session.Result == br, "duplicate completion refused");
        r = session.TryBegin()!;
        Check(session.Result is null, "new request clears previous overlay");
        Check(session.Complete(r, ar) == Attachment.Failed && session.Result is null, "wrong image result fails");
        r = session.TryBegin()!; session.Clear(); session.Show(b);
        Check(session.Complete(r, br) == Attachment.Stale, "clear/re-show same object invalidates occurrence");
        r = session.TryBegin()!;
        Check(session.Complete(r, new OverlayResult(b, [], 0)) == Attachment.Attached && session.Result!.Boxes.Count == 0, "empty is successful");
        r = session.TryBegin()!;
        Check(session.Complete(r, null, "injected error") == Attachment.Failed && session.Error == "injected error", "failure has no old overlay");
        bool rejected = await Task.Run(() => { try { session.Show(a); return false; } catch (InvalidOperationException) { return true; } });
        Check(rejected, "session refuses foreign thread");
        // Deterministic gate: clear before a pending worker publishes; no timing sleeps.
        TaskCompletionSource<OverlayResult> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        r = session.TryBegin()!;
        InferenceRequest gatedRequest = r;
        async Task<Attachment> FinishAsync() => session.Complete(gatedRequest, await gate.Task);
        Task<Attachment> pending = FinishAsync(); bool wasPending = !pending.IsCompleted;
        session.Clear(); gate.SetResult(br);
        Check(await pending == Attachment.Stale && wasPending && session.Current is null, "gated clear/late result");
        session.Show(a); r = session.TryBegin()!; session.BeginClose();
        Check(session.Closing && session.Busy && session.TryBegin() is null, "close prevents admission, preserves drain");
        Check(session.Complete(r, ar) == Attachment.Stale && !session.Busy && session.Current is null, "close drains without publication");
        Reject(() => session.Show(b), "closed session refuses image");
        YoloViewer viewer = new();
        Reject(() => viewer.Present(a, br), "viewer second identity guard");
        viewer.Present(a, ar);
        BitmapSource rendered = Render(viewer, 400, 400, 96);
        Check(IsBlack(rendered, 10, 10), "letterbox black");
        Check(Color(rendered, 50, 200).Take(3).SequenceEqual(new byte[] { 200, 30, 10 }), "original image rendered");
        Check(IsGreen(rendered, 100, 200), "box at expected display edge");
        BitmapSource dpi144 = Render(viewer, 400, 400, 144);
        Check(dpi144.PixelWidth == 600 && dpi144.PixelHeight == 600 && IsGreen(dpi144, 150, 300), "144DPI physical edge consistent");
        viewer.Present(null, null);
        Check(IsBlack(Render(viewer, 400, 400, 96), 100, 200), "clear removes image and old box");
        Console.WriteLine($"PASS {count} WPF/handwritten identity and render checks. No OpenCV/model or visible window.");
    }

    public static async Task Native(string model)
    {
        count = 0;
        string directory = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "verification", Guid.NewGuid().ToString("N"))).FullName;
        string path = Path.Combine(directory, "色彩图641x359.png");
        await Task.Run(() =>
        {
            byte[] packed = new byte[641 * 359 * 3];
            for (int y = 0; y < 359; y++)
            for (int x = 0; x < 641; x++)
            { int i = (y * 641 + x) * 3; packed[i] = (byte)(x % 256); packed[i + 1] = (byte)(y % 256); packed[i + 2] = (byte)((x + 2 * y) % 256); }
            using Mat image = OpenCvImageInput.ToMat(Bgr24Image.Create(641, 359, 641 * 3, packed));
            if (!Cv2.ImEncode(".png", image, out byte[] encoded)) throw new InvalidOperationException("Encode failed.");
            using FileStream file = new(path, FileMode.CreateNew); file.Write(encoded);
        });
        ImageSnapshot snapshot = await Task.Run(() => ImageSnapshot.Create(OpenCvImageInput.Load(path)));
        Check(snapshot.Bitmap.IsFrozen && snapshot.Bitmap.PixelWidth == 641 && snapshot.Bitmap.PixelHeight == 359, "native file to frozen original image");
        DisplaySession session = new(); session.Show(snapshot); InferenceRequest request = session.TryBegin()!;
        OverlayResult result = await Task.Run(() => OfflineDetector.Run(model, snapshot, .01f));
        Check(result.Kept == 5 && result.Boxes.Count == 5, "actual low-score gradient output, not objects");
        Check(result.Boxes.Select(b => b.Row).SequenceEqual(new[] { 3439, 3468, 3462, 3454, 3420 }), "old offline row order retained");
        Check(result.Image == snapshot, "real inference retains image occurrence");
        Check(session.Complete(request, result) == Attachment.Attached, "actual result UI attachment");
        Check(result.Boxes.All(b => b.SourceBox.X1 >= 0 && b.SourceBox.Y1 >= 0 && b.SourceBox.X2 <= 641 && b.SourceBox.Y2 <= 359), "source bounds after real NMS");
        YoloViewer viewer = new(); viewer.Present(snapshot, result);
        BitmapSource rendered = Render(viewer, 800, 600, 96);
        Check(rendered.PixelWidth == 800 && IsBlack(rendered, 0, 0), "actual original image uniformly laid out");
        string output = Path.Combine(directory, "overlay.png");
        PngBitmapEncoder encoder = new(); encoder.Frames.Add(BitmapFrame.Create(rendered));
        using (FileStream file = new(output, FileMode.CreateNew)) encoder.Save(file);
        Check(new FileInfo(output).Length > 0, "offscreen PNG exported to unique test directory");
        ImageSnapshot replacement = await Task.Run(() => ImageSnapshot.Create(OpenCvImageInput.Load(path)));
        request = session.TryBegin()!; session.Show(replacement);
        Check(session.Complete(request, result) == Attachment.Stale && session.Result is null, "same file reopened gets new occurrence");
        OverlayResult empty = await Task.Run(() => OfflineDetector.Run(model, replacement));
        Check(empty.Kept == 0 && empty.Boxes.Count == 0, "default score is normal empty result");
        Console.WriteLine($"PASS {count} actual OpenCV/CPU/WPF checks. Sessions disposed before rendering; no visible-window acceptance.");
        Console.WriteLine($"OFFSCREEN_PNG={output}");
    }

    private static ImageSnapshot Snapshot(int w, int h)
    {
        byte[] packed = new byte[w * h * 3];
        for (int i = 0; i < packed.Length; i += 3) { packed[i] = 200; packed[i + 1] = 30; packed[i + 2] = 10; }
        return ImageSnapshot.Create(new("handwritten", "test-only", 0, 3, Bgr24Image.Create(w, h, w * 3, packed)));
    }
    private static OverlayResult Hand(ImageSnapshot image) => new(image, [new(0, 1, .5f, new(5, 2, 15, 8))], 1);
    private static BitmapSource Render(YoloViewer viewer, int w, int h, double dpi)
    {
        viewer.Measure(new(w, h)); viewer.Arrange(new Rect(0, 0, w, h)); viewer.UpdateLayout();
        RenderTargetBitmap target = new((int)(w * dpi / 96), (int)(h * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        target.Render(viewer); target.Freeze(); return target;
    }
    private static byte[] Color(BitmapSource bitmap, int x, int y)
    { byte[] pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0); return pixel; }
    private static bool IsBlack(BitmapSource bitmap, int x, int y) => Color(bitmap, x, y).Take(3).All(c => c == 0);
    private static bool IsGreen(BitmapSource bitmap, int x, int y)
    { byte[] p = Color(bitmap, x, y); return p[1] > 100 && p[1] > p[0] && p[1] > p[2]; }
    private static void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException($"WPF check failed: {name}"); count++; }
    private static void Reject(Action action, string name)
    {
        try { action(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException) { count++; return; }
        throw new InvalidOperationException($"Expected rejection: {name}");
    }
}
