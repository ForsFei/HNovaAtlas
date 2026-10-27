namespace IndustrialYolo;

internal static class PreprocessingChecks
{
    private static int count;
    public static void Run()
    {
        count = 0;
        byte[] bytes = [0, 0, 255, 0, 255, 0, 17, 19, 255, 0, 0, 255, 255, 255, 23, 29];
        Bgr24Image image = Bgr24Image.Create(2, 2, 8, bytes);
        LetterboxTransform identity = LetterboxTransform.Create(2, 2, new(0, 0, 2, 2), 2, 2);
        PreparedInput tensor = ImagePreprocessor.Prepare(image, identity);
        Check(tensor.Shape.SequenceEqual(new long[] { 1, 3, 2, 2 }), "explicit NCHW shape");
        Check(tensor.Values.Length == 12, "three dense planes, no row padding");
        Check(tensor.Values.SequenceEqual(new float[] { 1, 0, 0, 1, 0, 1, 0, 1, 0, 0, 1, 1 }), "all RGB planes match hand-written 2x2 golden values");
        Check(image.Get(0, 1, 0) == 255 && image.Get(1, 1, 2) == 255, "BGR row stride read correctly");
        Check(bytes.SequenceEqual(new byte[] { 0, 0, 255, 0, 255, 0, 17, 19, 255, 0, 0, 255, 255, 255, 23, 29 }), "Prepare does not mutate caller bytes");
        Array.Fill(bytes, (byte)99);
        Check(image.Get(0, 0, 2) == 255 && image.Get(0, 0, 0) == 0, "BGR source copied at creation");
        Check(ImagePreprocessor.Prepare(image, identity).Values.SequenceEqual(tensor.Values), "caller mutation cannot change repeat preprocessing");
        float[] writableCopy = tensor.CopyValues();
        writableCopy[0] = 7;
        Check(tensor.Get(0, 0, 0) == 1, "tensor CopyValues is detached");
        Reject(() => ((IList<long>)tensor.Shape)[0] = 99, "shape is read-only");
        Check(identity is { PadLeft: 0, PadRight: 0, PadTop: 0, PadBottom: 0, ScaleX: 1, ScaleY: 1 }, "identity geometry");
        Check(identity.ToInput(new(0, 0, 2, 2)) == new Box(0, 0, 2, 2), "edge coordinate Width/Height allowed");

        Bgr24Image horizontal = Bgr24Image.Create(2, 1, 8, new byte[] { 0, 0, 255, 255, 0, 0, 77, 77 });
        LetterboxTransform square = LetterboxTransform.Create(2, 1, new(0, 0, 2, 1), 4, 4);
        PreparedInput padded = ImagePreprocessor.Prepare(horizontal, square);
        Check(square is { ResizedWidth: 4, ResizedHeight: 2, PadTop: 1, PadBottom: 1, PadLeft: 0, PadRight: 0 }, "wide image vertical letterbox");
        Check(AllPaddingRows(padded, 0, 3, 114 / 255f), "all channels padded and padding normalized once");
        Check(padded.Get(0, 1, 0) == 1 && padded.Get(2, 1, 0) == 0, "left red endpoint, RGB order");
        Check(padded.Get(0, 1, 3) == 0 && padded.Get(2, 1, 3) == 1, "right blue endpoint, border replication");
        Near(padded.Get(0, 1, 1), 191 / 255f, "half-pixel interpolation first interior red");
        Near(padded.Get(2, 1, 1), 64 / 255f, "half-pixel interpolation first interior blue");
        Near(padded.Get(0, 2, 2), 64 / 255f, "single-row source replicated vertically");
        Check(Enumerable.Range(0, 4).All(x => padded.Get(1, 1, x) == 0), "green plane stays zero");
        Check(padded.Values.ToArray().All(v => float.IsFinite(v) && v is >= 0 and <= 1), "entire tensor finite and 0..1");
        PreparedInput customPad = ImagePreprocessor.Prepare(horizontal, square, 0);
        Check(customPad.PaddingValue == 0 && AllPaddingRows(customPad, 0, 3, 0), "explicit custom padding");
        LetterboxTransform odd = LetterboxTransform.Create(2, 1, new(0, 0, 2, 1), 5, 5);
        Check(odd is { ResizedWidth: 5, ResizedHeight: 2, PadTop: 1, PadBottom: 2 }, "ToEven 2.5->2, odd remainder to bottom");
        LetterboxTransform tall = LetterboxTransform.Create(1, 2, new(0, 0, 1, 2), 5, 5);
        Check(tall is { ResizedWidth: 2, ResizedHeight: 5, PadLeft: 1, PadRight: 2 }, "tall image, odd remainder to right");
        LetterboxTransform noUpscale = LetterboxTransform.Create(2, 1, new(0, 0, 2, 1), 5, 5, false);
        Check(noUpscale is { ResizedWidth: 2, ResizedHeight: 1, PadLeft: 1, PadRight: 2, PadTop: 2, PadBottom: 2, IdealScale: 1, AllowUpscale: false }, "explicit no-upscale policy");
        LetterboxTransform thin = LetterboxTransform.Create(1, 8192, new(0, 0, 1, 8192), 1, 1);
        Check(thin is { ResizedWidth: 1, ResizedHeight: 1, PadLeft: 0, PadTop: 0 }, "thin side never rounds to zero");
        LetterboxTransform rectangular = LetterboxTransform.Create(4, 2, new(0, 0, 4, 2), 8, 6);
        Check(rectangular is { ResizedWidth: 8, ResizedHeight: 4, PadTop: 1, PadBottom: 1 }, "fixed non-square canvas");

        PreparedInput bothAxes = ImagePreprocessor.Prepare(image, LetterboxTransform.Create(2, 2, new(0, 0, 2, 2), 4, 4));
        Near(bothAxes.Get(0, 1, 1), 159 / 255f, "bilinear four-neighbor red golden value");
        Near(bothAxes.Get(1, 1, 1), 64 / 255f, "bilinear four-neighbor green golden value");
        Near(bothAxes.Get(2, 1, 1), 64 / 255f, "bilinear four-neighbor blue golden value");
        Bgr24Image blackWhite = Bgr24Image.Create(2, 1, 6, new byte[] { 0, 0, 0, 255, 255, 255 });
        PreparedInput down = ImagePreprocessor.Prepare(blackWhite, LetterboxTransform.Create(2, 1, new(0, 0, 2, 1), 1, 1));
        Near(down.Get(0, 0, 0), 128 / 255f, "downsample midpoint127.5 -> byte128 before normalization");
        Bgr24Image tieEven = Bgr24Image.Create(2, 1, 6, new byte[] { 0, 0, 0, 1, 1, 1 });
        Check(ImagePreprocessor.Prepare(tieEven, LetterboxTransform.Create(2, 1, new(0, 0, 2, 1), 1, 1)).Get(0, 0, 0) == 0, "explicit byte midpoint0.5 ToEven ->0");

        LetterboxTransform onePixelRoi = LetterboxTransform.Create(2, 2, new(1, 0, 1, 1), 3, 3);
        PreparedInput roiPixels = ImagePreprocessor.Prepare(image, onePixelRoi);
        Check(Enumerable.Range(0, 9).All(i => roiPixels.Values[i] == 0 && roiPixels.Values[9 + i] == 1 && roiPixels.Values[18 + i] == 0), "ROI samples only green source pixel, not outside neighbors");
        LetterboxTransform roi = LetterboxTransform.Create(640, 480, new(100, 50, 320, 200), 640, 640);
        Check(roi is { ResizedWidth: 640, ResizedHeight: 400, PadTop: 120, PadBottom: 120, ScaleX: 2, ScaleY: 2 }, "analytical ROI transform");
        Box original = new(120, 70, 220, 120);
        Box network = roi.ToInput(original);
        Check(network == new Box(40, 160, 240, 260), "forward golden coordinates subtract ROI before scale/pad");
        Check(roi.Restore(network) == new RestoredBox(original, false), "inverse golden coordinates restore ROI origin");
        Check(roi.Restore(new(0, 0, 20, 20)) == new RestoredBox(null, true), "padding-only box produces no original box");
        Check(roi.Restore(new(0, 100, 100, 120)).Original is null, "touching content edge has no area");
        Check(roi.Restore(new(-20, 100, 100, 200)) == new RestoredBox(new(100, 50, 150, 90), true), "partial/out-of-canvas box explicitly clipped to ROI content");
        Check(roi.Restore(new(-20, -20, 700, 700)) == new RestoredBox(new(100, 50, 420, 250), true), "large canvas-crossing box clipped to entire ROI");
        LetterboxTransform rounded = LetterboxTransform.Create(3, 2, new(0, 0, 3, 2), 5, 5);
        Check(rounded is { ResizedWidth: 5, ResizedHeight: 3, PadTop: 1, PadBottom: 1, ScaleY: 1.5 }, "raster rounding changes actual Y ratio");
        Near(rounded.ScaleX, 5d / 3, "actual X ratio");
        Check(rounded.ToInput(new(0, 0, 3, 2)) == new Box(0, 1, 5, 4), "actual edge transform uses rounded size");
        Check(rounded.Restore(new(0, 1, 5, 4)) == new RestoredBox(new(0, 0, 3, 2), false), "rounded edges restore correctly");
        Near((4 - rounded.PadTop) / rounded.IdealScale, 1.8, "using ideal scale instead would yield wrong2->1.8 in our policy");
        bool roundTrips = true;
        foreach ((int width, int height) in new[] { (3, 2), (2, 7), (7, 3), (19, 11), (640, 480), (1, 100) })
        foreach (int side in new[] { 1, 5, 17, 32, 63, 640 })
        {
            LetterboxTransform plan = LetterboxTransform.Create(width + 6, height + 8, new(3, 4, width, height), side, side);
            Box box = new(3 + width * .1, 4 + height * .2, 3 + width * .8, 4 + height * .9);
            Box? restored = plan.Restore(plan.ToInput(box)).Original;
            roundTrips &= restored is not null && Math.Abs(restored.X1 - box.X1) < 1e-9 && Math.Abs(restored.Y1 - box.Y1) < 1e-9 &&
                Math.Abs(restored.X2 - box.X2) < 1e-9 && Math.Abs(restored.Y2 - box.Y2) < 1e-9;
        }
        Check(roundTrips, "36 heterogeneous round trips, supplemental to independent golden coordinates");

        Reject(() => Bgr24Image.Create(0, 1, 3, new byte[3]), "zero source width");
        Reject(() => Bgr24Image.Create(8193, 1, 3, []), "source side upper bound");
        Reject(() => Bgr24Image.Create(8192, 8192, 24576, []), "source pixel budget");
        Reject(() => Bgr24Image.Create(2, 2, 5, new byte[10]), "stride too short");
        Reject(() => Bgr24Image.Create(2, 2, -8, []), "negative/bottom-up stride unsupported");
        Reject(() => Bgr24Image.Create(1, 2, int.MaxValue, []), "stride byte budget/overflow");
        Reject(() => Bgr24Image.Create(2, 2, 8, new byte[15]), "buffer too short");
        Reject(() => Bgr24Image.Create(2, 2, 8, new byte[17]), "buffer tail not silently accepted");
        Reject(() => LetterboxTransform.Create(2, 2, new(-1, 0, 1, 1), 4, 4), "negative ROI origin");
        Reject(() => LetterboxTransform.Create(2, 2, new(0, 0, 0, 1), 4, 4), "zero ROI width");
        Reject(() => LetterboxTransform.Create(2, 2, new(1, 0, 2, 1), 4, 4), "ROI outside source");
        Reject(() => LetterboxTransform.Create(2, 2, new(int.MaxValue, 0, int.MaxValue, 1), 4, 4), "ROI addition uses long, cannot overflow");
        Reject(() => LetterboxTransform.Create(2, 2, new(0, 0, 2, 2), 0, 4), "zero input dimension");
        Reject(() => LetterboxTransform.Create(2, 2, new(0, 0, 2, 2), 2049, 4), "input dimension budget");
        Reject(() => ImagePreprocessor.Prepare(image, square), "image dimensions must match transform");
        Reject(() => ImagePreprocessor.Prepare(null!, identity), "null image");
        Reject(() => ImagePreprocessor.Prepare(image, null!), "null transform");
        Reject(() => roi.ToInput(new(99, 50, 110, 60)), "box partly outside ROI rejected on forward path");
        Reject(() => roi.ToInput(new(100, 50, 100, 60)), "zero-area forward box");
        Reject(() => roi.ToInput(null!), "null forward box");
        Reject(() => roi.Restore(new(double.NaN, 1, 10, 10)), "nonfinite inverse input");
        Reject(() => roi.Restore(new(0, 0, double.PositiveInfinity, 10)), "infinite inverse input");
        Reject(() => roi.Restore(new(10, 1, 0, 10)), "inverted inverse input");
        Reject(() => roi.Restore(new(0, 0, 1_000_001, 10)), "inverse coordinate resource bound");
        Reject(() => roi.Restore(null!), "null inverse box");
        Reject(() => roi.Restore(new(0, 120, double.Epsilon, 121)), "unrepresentable inverse box cannot become zero area after adding ROI origin");
        Reject(() => tensor.Get(3, 0, 0), "invalid RGB channel");
        Reject(() => tensor.Get(0, -1, 0), "invalid tensor row");
        Reject(() => tensor.Get(0, 0, 2), "invalid tensor column");
        Reject(() => image.Get(2, 0, 0), "source pixel out of bounds");
        Reject(() => image.Get(0, 0, 3), "source channel out of bounds");
        Console.WriteLine($"PASS {count} preprocessing/geometry checks, including36 round trips in one check. Pure C# synthetic pixels, NOT ONNX/OpenCV parity or inference.");
    }

    private static bool AllPaddingRows(PreparedInput input, int first, int second, float value) =>
        Enumerable.Range(0, 3).All(c => Enumerable.Range(0, input.Transform.InputWidth)
            .All(x => input.Get(c, first, x) == value && input.Get(c, second, x) == value));
    private static void Near(double actual, double expected, string name) => Check(Math.Abs(actual - expected) < 1e-7, name);
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Preprocessing check failed: {name}");
        count++;
    }
    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException) { count++; return; }
        throw new InvalidOperationException($"Expected preprocessing rejection: {name}");
    }
}
