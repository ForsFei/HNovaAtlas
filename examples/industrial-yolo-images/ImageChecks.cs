using IndustrialYolo;
using IndustrialYolo.Onnx;
using OpenCvSharp;

namespace IndustrialYolo.Images;

internal static class ImageChecks
{
    private static int count;
    public static void Managed()
    {
        count = 0;
        ImageGeometry wide = ImageGeometry.Create(641, 359);
        Check(wide.ResizedWidth == 416 && wide.ResizedHeight == 232, "upstream truncation, not rounded233");
        Near(wide.Ratio, 416d / 641, "ideal ratio");
        Check(wide.PadRight == 0 && wide.PadBottom == 184, "left-top content, bottom fill");
        Check(wide.RasterScaleY != wide.Ratio && wide.RasterScaleX == wide.Ratio, "raster and reference ratios distinct");
        ImageGeometry tall = ImageGeometry.Create(359, 641);
        Check(tall.ResizedWidth == 232 && tall.ResizedHeight == 416 && tall.PadRight == 184, "tall image right fill");
        ImageGeometry up = ImageGeometry.Create(8, 4);
        Check(up.ResizedWidth == 416 && up.ResizedHeight == 208 && up.Ratio == 52, "upscaling allowed by fixed policy");
        ImageGeometry down = ImageGeometry.Create(832, 416);
        Check(down.Ratio == .5 && down.ResizedWidth == 416 && down.ResizedHeight == 208, "downscale exact ratio");
        Box original = new(20, 40, 60, 80);
        Check(down.ToNetwork(original) == new Box(10, 20, 30, 40), "forward source model coordinates");
        Check(down.ToReferenceSource(new(10, 20, 30, 40)) == original, "unclipped model coordinates divide ideal ratio");
        Check(down.ToVisibleSource(new(-10, 200, 20, 220)) == new VisibleImageBox(new(0, 400, 40, 416), true), "clip actual network content before visible inverse");
        Check(down.ToVisibleSource(new(0, 208, 10, 220)).Original is null, "padding-only source has no visible box");
        Check(wide.ToVisibleSource(new(0, 232.1, 10, 232.5)).Original is null, "floor band is actual padding, even if ideal inverse lies inside original bounds");
        Near(wide.ToReferenceSource(new(0, 0, 416, 232)).Y2, 232 * 641d / 416, "ideal ratio, not forcing actual resized height to full original height");
        Box fullForward = wide.ToNetwork(new(0, 0, 641, 359));
        Check(fullForward.Y2 > wide.ResizedHeight && fullForward.Y2 < 233, "reference labels may extend beyond rounded raster edge");
        bool roundTrips = true;
        foreach ((int w, int h) in new[] { (8, 4), (641, 359), (359, 641), (832, 832), (1920, 1080), (1, 1) })
        {
            ImageGeometry g = ImageGeometry.Create(w, h); Box box = new(w * .1, h * .2, w * .8, h * .9);
            Box restored = g.ToReferenceSource(g.ToNetwork(box));
            roundTrips &= Math.Abs(restored.X1 - box.X1) < 1e-9 && Math.Abs(restored.Y2 - box.Y2) < 1e-9;
        }
        Check(roundTrips, "six ideal-coordinate round trips in one check");
        Reject(() => ImageGeometry.Create(0, 1), "zero dimensions");
        Reject(() => ImageGeometry.Create(8193, 1), "side limit");
        Reject(() => ImageGeometry.Create(8192, 8192), "pixel budget");
        Reject(() => ImageGeometry.Create(8192, 1), "upstream truncation to zero rejected, not silently clamped to1");
        Reject(() => down.ToNetwork(new(-1, 0, 10, 10)), "source forward bounds");
        Reject(() => down.ToReferenceSource(new(double.NaN, 0, 10, 10)), "nonfinite network geometry");
        Reject(() => down.ToReferenceSource(new(1, 0, 0, 10)), "inverted network geometry");
        Reject(() => down.ToReferenceSource(new(0, 0, 1e7, 10)), "mapped coordinate teaching bound");
        Reject(() => down.ToVisibleSource(null!), "null network box");

        PreparedImage prepared = ManagedBlack(832, 832);
        float[] raw = new float[3549 * 85];
        Set(raw, 0, .5f, .5f, 1, 1, .9f);
        Set(raw, 1, 1.5f, .5f, 1, 1, .8f);
        ImageReport report = ImagePostprocessor.ProcessValues(raw, prepared, new(.1f, .25));
        Check(report.Candidates.Count == 2 && report.Detections.Count == 2, "NMS on source coords, not network coords");
        Near(YoloXPostprocessor.UpstreamInclusiveNmsIou(new(0, 0, 1, 1), new(1, 0, 2, 1)), 1d / 3, "wrong network-space overlap would suppress");
        Near(YoloXPostprocessor.UpstreamInclusiveNmsIou(new(0, 0, 2, 2), new(2, 0, 4, 2)), .2, "correct source-space overlap retained");
        Check(report.Detections[0].Candidate.NetworkRow.RowIndex == 0 && report.Visible == 2, "row identity preserved across spaces");
        Check(report.Candidates[0].ReferenceSource.X2 > report.Candidates[0].NetworkRow.NetworkBox.X2, "source/network boxes separately named");
        raw[0] = 999;
        Check(report.Candidates[0].NetworkRow.NetworkBox.X1 < 1, "report detached from raw caller array");
        Reject(() => ((IList<ImageDetection>)report.Detections).Clear(), "read-only mapped detections");
        Reject(() => ImagePostprocessor.ProcessValues(new float[3], prepared, new()), "raw shape guard reused");
        Reject(() => ImagePostprocessor.ProcessValues(new float[3549 * 85], prepared, new(float.NaN)), "invalid score setting");
        Reject(() => ImagePostprocessor.ProcessValues(new float[3549 * 85], prepared, new(.1f, 2)), "invalid NMS setting");
        Reject(() => ImagePostprocessor.ProcessValues([], null!, new()), "null prepared context");
        Console.WriteLine($"PASS {count} managed image-geometry/source-NMS checks. No codec, resize or model run.");
    }

    public static void Native(string model)
    {
        count = 0;
        string directory = FixtureFiles.CreateDirectory();
        string chart = FixtureFiles.Chart(directory);
        LoadedImage loaded = OpenCvImageInput.Load(chart);
        Check(Cv2.GetVersionString() == "4.13.0", "actual OpenCV version");
        Check(loaded.Bgr.Width == 641 && loaded.Bgr.Height == 359 && loaded.SourceChannels == 3, "actual PNG decode dimensions/type");
        Check(loaded.Bgr.Get(1, 1, 0) == 1 && loaded.Bgr.Get(1, 1, 1) == 1 && loaded.Bgr.Get(1, 1, 2) == 3, "lossless chart golden BGR pixel");
        Check(loaded.EncodedSha256.Length == 64 && loaded.Path.Contains("色彩图", StringComparison.Ordinal), "unicode file path and fingerprint");
        byte[] encoded = File.ReadAllBytes(chart);
        LoadedImage owned = OpenCvImageInput.Decode(encoded);
        Array.Fill(encoded, (byte)0);
        Check(owned.Bgr.Get(1, 1, 2) == 3, "decoded snapshot survives caller encoded-buffer mutation");
        using (Mat gray = new(11, 17, MatType.CV_8UC1, new Scalar(37)))
        {
            LoadedImage value = OpenCvImageInput.Decode(FixtureFiles.Encode(gray, ".png"));
            Check(value.SourceChannels == 1 && Enumerable.Range(0, 3).All(c => value.Bgr.Get(0, 0, c) == 37), "8-bit gray explicitly expanded to BGR");
        }
        using (Mat jpeg = new(13, 23, MatType.CV_8UC3, new Scalar(10, 80, 190)))
        {
            LoadedImage value = OpenCvImageInput.Decode(FixtureFiles.Encode(jpeg, ".jpg"));
            Check(value.Bgr.Width == 23 && value.Bgr.Height == 13, "actual JPEG decode, no lossless pixel assertion");
        }
        using (Mat alpha = new(2, 2, MatType.CV_8UC4, Scalar.All(255)))
            Reject(() => OpenCvImageInput.Decode(FixtureFiles.Encode(alpha, ".png")), "alpha PNG requires explicit external policy");
        using (Mat depth = new(2, 2, MatType.CV_16UC1, Scalar.All(1000)))
            Reject(() => OpenCvImageInput.Decode(FixtureFiles.Encode(depth, ".png")), "16-bit input not silently reduced");
        Reject(() => OpenCvImageInput.Decode([1, 2, 3, 4]), "unsupported signature");
        Reject(() => OpenCvImageInput.Decode([]), "empty bytes");
        Reject(() => OpenCvImageInput.Decode(new byte[OpenCvImageInput.MaximumEncodedBytes + 1]), "encoded size bound before decode");
        Reject(() => OpenCvImageInput.Load(Path.Combine(directory, "absent.png")), "missing file not empty detection");
        string disguised = FixtureFiles.Write(directory, "image.dat", File.ReadAllBytes(chart));
        Check(OpenCvImageInput.Load(disguised).Bgr.Width == 641, "file content, not extension, selects decoder");
        Bgr24Image roiCopy;
        using (Mat parent = new(3, 5, MatType.CV_8UC3, new Scalar(7, 11, 19)))
        using (Mat roi = parent.SubMat(new Rect(1, 1, 2, 2)))
        {
            Check(!roi.IsContinuous(), "actual noncontinuous ROI");
            roiCopy = OpenCvImageInput.CopyBgr(roi);
        }
        Check(roiCopy.Get(1, 1, 0) == 7 && roiCopy.Get(1, 1, 2) == 19, "row copy detached after parent/ROI disposal");
        using (Mat wrong = new(2, 2, MatType.CV_8UC1)) Reject(() => OpenCvImageInput.CopyBgr(wrong), "Mat bridge rejects wrong type");
        PreparedImage prepared = OpenCvImageInput.Prepare(loaded);
        Check(prepared.Geometry.ResizedWidth == 416 && prepared.Geometry.ResizedHeight == 232, "actual native resize target");
        Check(prepared.Tensor.Shape.SequenceEqual(new long[] { 1, 3, 416, 416 }), "actual dense tensor shape");
        int plane = 416 * 416;
        Check(prepared.Tensor.Values[232 * 416] == 114 && prepared.Tensor.Values[3 * plane - 1] == 114, "bottom canvas fill survives byte-to-float packing");
        using (Mat red = new(4, 8, MatType.CV_8UC3, new Scalar(0, 0, 255)))
        {
            PreparedImage value = OpenCvImageInput.Prepare(OpenCvImageInput.Decode(FixtureFiles.Encode(red, ".png")));
            Check(value.Tensor.Values[0] == 0 && value.Tensor.Values[plane] == 0 && value.Tensor.Values[2 * plane] == 255, "resized BGR0..255, no RGB swap or /255");
            Check(value.Tensor.Values[208 * 416] == 114, "upscale height208 then bottom fill");
        }
        string identity = FixtureFiles.Identity(directory);
        PreparedImage identityInput = OpenCvImageInput.Prepare(OpenCvImageInput.Load(identity));
        Check(identityInput.Tensor.Sha256 == NumericReference.InputSha256, "r1 file/resize path preserves entire old input hash");
        using YoloXNanoSession session = new(model);
        Check(session.RuntimeVersion == "1.27.0", "actual CPU runtime");
        YoloXRawOutput identityRaw = session.Run(identityInput.Tensor);
        NumericReference.CheckSamples(identityRaw);
        Check(true, "r1 file path agrees with frozen Python output samples");
        string beforeHash = prepared.Tensor.Sha256;
        YoloXRawOutput raw = session.Run(prepared.Tensor);
        ImageReport report = ImagePostprocessor.Process(raw, prepared, new(.01f, .45));
        Check(report.Rows.Count == 3549, "actual resized file inference decoded");
        Check(report.Candidates.Count == report.Detections.Count + report.Suppressions.Count, "source-space NMS accounting");
        Check(report.Detections.All(d => d.VisibleSource.Original is null ||
            d.VisibleSource.Original is { X1: >= 0, Y1: >= 0, X2: <= 641, Y2: <= 359 }), "all mapped visible source bounds");
        Check(prepared.Tensor.Sha256 == beforeHash && loaded.Bgr.Get(1, 1, 2) == 3, "inference/processing leave owned pixels/tensor unchanged");
        session.Dispose();
        Check(report.Prepared.Geometry.Width == 641 && report.Rows[0].NetworkBox is not null, "managed report survives native session disposal");
        Console.WriteLine($"PASS {count} actual codec/OpenCV/CPU-image checks. Owned chart, not object-quality or EXIF/color-management acceptance.");
        Console.WriteLine($"REFERENCE_IMAGE={chart}");
        Console.WriteLine($"IMAGE_TENSOR_SHA256={prepared.Tensor.Sha256}");
        Console.WriteLine($"Candidates={report.Candidates.Count} Kept={report.Detections.Count} Visible={report.Visible}");
    }

    private static PreparedImage ManagedBlack(int width, int height)
    {
        Bgr24Image source = Bgr24Image.Create(width, height, width * 3, new byte[width * height * 3]);
        Bgr24Image canvas = Bgr24Image.Create(416, 416, 1248, new byte[416 * 416 * 3]);
        return new(new("managed-black", "not-a-file", 0, 3, source), ImageGeometry.Create(width, height), YoloXInput.PrepareNoResize(canvas));
    }
    private static void Set(float[] raw, int row, float cx, float cy, float w, float h, float score)
    {
        YoloXGrid grid = YoloXPostprocessor.GridAt(row); int i = row * 85;
        raw[i] = cx / grid.Stride - grid.X; raw[i + 1] = cy / grid.Stride - grid.Y;
        raw[i + 2] = MathF.Log(w / grid.Stride); raw[i + 3] = MathF.Log(h / grid.Stride);
        raw[i + 4] = 1; raw[i + 5] = score;
    }
    private static void Near(double actual, double expected, string name) => Check(Math.Abs(actual - expected) < 1e-9, name);
    private static void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException($"Image check failed: {name}"); count++; }
    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or NotSupportedException) { count++; return; }
        throw new InvalidOperationException($"Expected image rejection: {name}");
    }
}
