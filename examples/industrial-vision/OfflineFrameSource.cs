namespace IndustrialVision;

public readonly record struct FrameStamp(Guid Session, ulong Number);
public sealed record OwnedFrame(FrameStamp Stamp, Mono8Image Image);

public sealed class FrameLease : IDisposable
{
    private Func<OwnedFrame>? copy;
    private Action? release;
    public FrameStamp Stamp { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    internal FrameLease(FrameStamp stamp, int width, int height, int stride, Func<OwnedFrame> copy, Action release)
    { Stamp = stamp; Width = width; Height = height; Stride = stride; this.copy = copy; this.release = release; }
    public OwnedFrame CopyToOwned() => copy is { } action ? action() : throw new ObjectDisposedException(nameof(FrameLease));
    public void Dispose()
    {
        Action? action = release; copy = null; release = null; action?.Invoke();
    }
}

// A synchronous, single-caller replay source. One reusable buffer, one outstanding lease.
// Its close-invalidates-lease policy is a teaching choice, NOT a camera SDK compatibility promise.
public sealed class OfflineFrameSource : IDisposable
{
    private readonly Mono8Image[] fixtures;
    private readonly byte[] buffer;
    private Guid activeLease;
    private bool disposed;
    private ulong number;
    public bool IsOpen { get; private set; }
    public Guid Session { get; private set; }
    public int Width => fixtures[0].Width;
    public int Height => fixtures[0].Height;
    public int Stride { get; }
    public int OutstandingLeases => activeLease == Guid.Empty ? 0 : 1;
    public ulong FramesAcquired => number;

    public OfflineFrameSource(IEnumerable<Mono8Image> fixtures, int paddingX = 2)
    {
        ArgumentNullException.ThrowIfNull(fixtures);
        this.fixtures = fixtures.Take(17).ToArray();
        if (this.fixtures.Length is < 1 or > 16 || this.fixtures.Any(x => x is null))
            throw new ArgumentException("One to sixteen nonnull fixtures required.");
        if (this.fixtures.Any(x => x.Width != Width || x.Height != Height) || paddingX is < 0 or > 64)
            throw new ArgumentException("Matching dimensions and teaching padding range 0..64 required.");
        Stride = checked(Width + paddingX);
        long bytes = (long)Stride * Height;
        if (bytes > 8_388_608) throw new ArgumentException("Teaching raw buffer cap exceeded.");
        buffer = new byte[(int)bytes];
    }
    public void Open()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsOpen) throw new InvalidOperationException("Already open.");
        IsOpen = true; Session = Guid.NewGuid(); number = 0;
    }
    public FrameLease Acquire()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsOpen || activeLease != Guid.Empty || number == ulong.MaxValue)
            throw new InvalidOperationException("Open source, returned lease and unexhausted counter required.");
        Mono8Image fixture = fixtures[(int)(number % (ulong)fixtures.Length)];
        byte[] packed = fixture.CopyPixels();
        Array.Fill(buffer, (byte)238); // Visible padding sentinel; never an image pixel.
        for (int y = 0; y < Height; y++) packed.AsSpan(y * Width, Width).CopyTo(buffer.AsSpan(y * Stride, Width));
        FrameStamp stamp = new(Session, ++number);
        Guid leaseId = Guid.NewGuid(); activeLease = leaseId;
        return new(stamp, Width, Height, Stride, () => Copy(leaseId, stamp), () => Return(leaseId));
    }
    private OwnedFrame Copy(Guid leaseId, FrameStamp stamp)
    {
        if (disposed || !IsOpen || activeLease != leaseId || stamp.Session != Session)
            throw new InvalidOperationException("Borrowed buffer lease is no longer valid.");
        return new(stamp, Mono8Image.CopyFromBuffer(Width, Height, Stride, buffer, PixelEncoding.Mono8, complete: true));
    }
    private void Return(Guid leaseId) { if (activeLease == leaseId) activeLease = Guid.Empty; }
    public void Close() { IsOpen = false; activeLease = Guid.Empty; }
    public void Dispose() { Close(); disposed = true; }
}
