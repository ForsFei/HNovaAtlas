namespace IndustrialVision;

public static class VisionChecks
{
    private static int count;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine($"PASS {++count}: {label}");
    }
    private static void Reject<T>(Action action, string label) where T : Exception
    {
        try { action(); } catch (T) { Check(true, label); return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}: {label}");
    }
    public static void Run()
    {
        count = 0;
        Mono8Image image = PlainPgm.LoadFixture();
        Check(image.Width == 8 && image.Height == 6, "Committed P2 fixture dimensions");
        Check(image.GetPixel(1, 1) == 220 && image.GetPixel(5, 1) == 180, "Known independent bright patches");
        PixelStatistics full = image.Measure(new(0, 0, 8, 6), 200);
        Check(full.Count == 48 && full.Min == 0 && full.Max == 255, "Known fixture range and sample count");
        Check(Math.Abs(full.Mean - 51.3125) < 1e-10, "Fixture mean agrees with independently summed 2463/48");
        Check(full.AtOrAboveThreshold == 5 && full.BlackPixels == 1 && full.WhitePixels == 1, "Inclusive threshold and endpoint counts, not defect labels");
        Check(image.Measure(new(1, 1, 2, 2), 200) is { Count: 4, Mean: 220, AtOrAboveThreshold: 4 }, "Left ROI statistics");
        Check(image.Measure(new(5, 1, 2, 2), 200) is { Mean: 180, AtOrAboveThreshold: 0 }, "Right ROI is not conflated with left");
        ImageCrop crop = image.Crop(new(1, 1, 2, 2));
        Check(crop.SourceRegion == new PixelRoi(1, 1, 2, 2) && crop.Image.Width == 2
            && crop.Image.GetPixel(0, 0) == 220, "Crop records parent region and resets local origin");
        byte[] exported = image.CopyPixels(); exported[0] = 99;
        Check(image.GetPixel(0, 0) == 16, "Exported byte array cannot mutate owned image");
        byte[] input = [1, 2, 3, 4]; Mono8Image copied = Mono8Image.FromPacked(2, 2, input); input[0] = 99;
        Check(copied.GetPixel(0, 0) == 1, "Packed image defensively copies caller buffer");
        Reject<ArgumentOutOfRangeException>(() => image.GetPixel(-1, 0), "Negative pixel coordinate rejected");
        Reject<ArgumentOutOfRangeException>(() => image.GetPixel(8, 0), "Right edge is exclusive");
        Reject<ArgumentOutOfRangeException>(() => image.GetPixel(0, 6), "Bottom edge is exclusive");
        Reject<ArgumentException>(() => image.Crop(new(0, 0, 0, 1)), "Empty ROI rejected");
        Reject<ArgumentException>(() => image.Measure(new(-1, 0, 1, 1), 0), "Negative ROI origin rejected");
        Reject<ArgumentException>(() => image.Crop(new(7, 5, 2, 1)), "Out-of-bounds ROI rejected");
        Reject<ArgumentException>(() => image.Crop(new(int.MaxValue, 0, int.MaxValue, 1)), "ROI bounds arithmetic does not wrap");
        Reject<ArgumentException>(() => Mono8Image.FromPacked(0, 1, []), "Zero image width rejected");
        Reject<ArgumentException>(() => Mono8Image.FromPacked(-1, -1, []), "Negative dimensions rejected");
        Reject<ArgumentException>(() => Mono8Image.FromPacked(int.MaxValue, int.MaxValue, []), "Huge product checked before allocation");
        Reject<ArgumentException>(() => Mono8Image.FromPacked(2, 2, [1, 2, 3]), "Truncated packed image rejected");
        Reject<ArgumentException>(() => Mono8Image.FromPacked(1, 1, [1, 2]), "Packed count is exact, not guessed");
        byte[] padded = [1, 2, 3, 238, 238, 4, 5, 6];
        Mono8Image unpacked = Mono8Image.CopyFromBuffer(3, 2, 5, padded, PixelEncoding.Mono8, true);
        Check(unpacked.CopyPixels().SequenceEqual(new byte[] { 1, 2, 3, 4, 5, 6 }), "Stride-aware row copy excludes padding; final padding not required");
        padded[0] = 99;
        Check(unpacked.GetPixel(0, 0) == 1, "Row copy owns its pixels");
        Reject<ArgumentException>(() => Mono8Image.CopyFromBuffer(3, 2, 2, padded, PixelEncoding.Mono8, true), "Stride shorter than row rejected");
        Reject<ArgumentException>(() => Mono8Image.CopyFromBuffer(3, 2, 5, new byte[7], PixelEncoding.Mono8, true), "Short final row rejected");
        Reject<ArgumentException>(() => Mono8Image.CopyFromBuffer(1, 2, int.MaxValue, [], PixelEncoding.Mono8, true), "Stride product cannot wrap buffer validation");
        Reject<ArgumentException>(() => Mono8Image.CopyFromBuffer(1, 1, -1, [0], PixelEncoding.Mono8, true), "Negative pitch not silently reinterpreted");
        Reject<ArgumentException>(() => Mono8Image.CopyFromBuffer(1, 1, 1, [0], PixelEncoding.Mono8), "Completeness must be explicitly declared");
        foreach (PixelEncoding format in new[] { PixelEncoding.Unknown, PixelEncoding.Mono12Packed, PixelEncoding.BayerRG8, (PixelEncoding)99 })
            Reject<ArgumentException>(() => Mono8Image.CopyFromBuffer(1, 1, 1, [0], format, true), $"Unsupported format refused, not re-labeled Mono8: {format}");

        Check(PlainPgm.Parse("P2\r\n# test\r\n2 1 # size\r\n255\r\n0 255\r\n").CopyPixels().SequenceEqual(new byte[] { 0, 255 }), "P2 comments and CRLF parsed");
        Reject<FormatException>(() => PlainPgm.Parse("P5 1 1 255 0"), "Binary P5 not treated as plain P2");
        Reject<FormatException>(() => PlainPgm.Parse("P2 1 1 15 15"), "Unsupported maxval does not silently rescale");
        Reject<FormatException>(() => PlainPgm.Parse("P2 2 1 255 0"), "Short raster rejected");
        Reject<FormatException>(() => PlainPgm.Parse("P2 1 1 255 0 1"), "Extra raster rejected");
        Reject<FormatException>(() => PlainPgm.Parse("P2 1 1 255 -1"), "Negative sample rejected");
        Reject<FormatException>(() => PlainPgm.Parse("P2 1 1 255 256"), "Sample above 255 rejected");
        Reject<FormatException>(() => PlainPgm.Parse("P2 1 1 255 1.5"), "Noninteger sample rejected");
        Reject<ArgumentException>(() => PlainPgm.Parse(new string(' ', PlainPgm.MaxTextLength + 1)), "Text resource cap enforced");

        Mono8Image second = Mono8Image.FromPacked(8, 6, Enumerable.Repeat((byte)80, 48).ToArray());
        using OfflineFrameSource source = new([image, second]);
        Reject<InvalidOperationException>(() => source.Acquire(), "Cannot acquire before Open");
        source.Open(); Guid firstSession = source.Session;
        Check(source.IsOpen && firstSession != Guid.Empty && source.OutstandingLeases == 0, "Explicit offline open establishes a session only");
        Reject<InvalidOperationException>(() => source.Open(), "Double open rejected");
        FrameLease firstLease = source.Acquire(); OwnedFrame first = firstLease.CopyToOwned();
        Check(firstLease.Stride == 10 && first.Stamp == new FrameStamp(firstSession, 1)
            && first.Image.CopyPixels().SequenceEqual(image.CopyPixels()), "Borrowed padded buffer copied with identity and tight rows");
        Check(source.OutstandingLeases == 1 && source.FramesAcquired == 1, "One outstanding borrowed result");
        Reject<InvalidOperationException>(() => source.Acquire(), "Buffer cannot be reused while leased");
        firstLease.Dispose(); firstLease.Dispose();
        Check(source.OutstandingLeases == 0, "Lease disposal idempotently returns one buffer");
        Reject<ObjectDisposedException>(() => firstLease.CopyToOwned(), "Disposed lease cannot access pixels");
        using (FrameLease lease = source.Acquire())
        {
            OwnedFrame next = lease.CopyToOwned();
            Check(next.Stamp.Number == 2 && next.Image.GetPixel(0, 0) == 80, "Second fixture reuses source buffer with new frame number");
            Check(first.Image.GetPixel(0, 0) == 16 && first.Image.GetPixel(1, 1) == 220, "Owned old frame survives source buffer overwrite");
        }
        FrameLease stale = source.Acquire(); source.Close();
        Check(!source.IsOpen && source.OutstandingLeases == 0, "Teaching Close invalidates current lease");
        Reject<InvalidOperationException>(() => stale.CopyToOwned(), "Closed-source lease cannot copy");
        source.Open(); FrameLease current = source.Acquire();
        Check(source.Session != firstSession && current.Stamp.Number == 1, "Reopen creates a new session before resetting frame count");
        stale.Dispose();
        Check(source.OutstandingLeases == 1, "Old lease return cannot free a new-session lease");
        current.Dispose();
        try { using FrameLease lease = source.Acquire(); throw new FormatException("scripted processing failure"); }
        catch (FormatException) { }
        Check(source.OutstandingLeases == 0, "Using returns lease even when processing throws");
        source.Dispose(); source.Dispose();
        Reject<ObjectDisposedException>(() => source.Open(), "Disposed source cannot reopen");
        Check(first.Image.GetPixel(1, 1) == 220, "Owned managed copy outlives source disposal");
        Reject<ArgumentException>(() => new OfflineFrameSource([]), "No fixture source rejected");
        Reject<ArgumentException>(() => new OfflineFrameSource([image, Mono8Image.FromPacked(1, 1, [0])]), "Mixed image dimensions rejected in one replay source");
        Reject<ArgumentException>(() => new OfflineFrameSource([image], -1), "Negative padding rejected");
        Reject<ArgumentException>(() => new OfflineFrameSource(Enumerable.Repeat(image, 17)), "Fixture list cap enforced");
        Console.WriteLine($"{count} offline vision checks passed; synthetic P2/Mono8 and managed buffer contracts only, no camera/SDK.");
    }
    public static void Demo()
    {
        using OfflineFrameSource source = new([PlainPgm.LoadFixture()]); source.Open();
        OwnedFrame frame;
        using (FrameLease lease = source.Acquire()) frame = lease.CopyToOwned();
        source.Close();
        PixelStatistics s = frame.Image.Measure(new(0, 0, frame.Image.Width, frame.Image.Height), 200);
        Console.WriteLine($"Owned frame {frame.Stamp.Number}: {frame.Image.Width}x{frame.Image.Height}, mean={s.Mean:F4}, >=200={s.AtOrAboveThreshold}");
        Console.WriteLine($"Source closed; pixel(1,1) remains {frame.Image.GetPixel(1, 1)}, outstanding leases={source.OutstandingLeases}");
        Console.WriteLine("Synthetic fixture statistics only; not a defect decision, calibration, exposure or real acquisition.");
    }
}
