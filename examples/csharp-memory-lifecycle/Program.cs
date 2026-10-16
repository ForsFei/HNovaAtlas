var owned = new TrackingStream([42]);
try
{
    using var session = new StreamSession(owned, ownsStream: true);
    Check(session.ReadFirstByte() == 42, "应读到第一个字节");
    throw new InvalidOperationException("模拟处理失败");
}
catch (InvalidOperationException)
{
    // using 在异常离开作用域时已执行 Dispose。
}
Check(owned.WasDisposed, "拥有的流应在异常路径关闭");

var borrowed = new TrackingStream([7]);
var borrowedSession = new StreamSession(borrowed, ownsStream: false);
Check(borrowedSession.ReadFirstByte() == 7, "借用流应可读取");
borrowedSession.Dispose();
borrowedSession.Dispose(); // 重复清理应安全
Check(!borrowed.WasDisposed, "借用者不应关闭流");
try
{
    borrowedSession.ReadFirstByte();
    throw new Exception("应拒绝使用已释放的包装器");
}
catch (ObjectDisposedException) { }
borrowed.Dispose(); // 创建者负责关闭
Check(borrowed.WasDisposed, "最终所有者应关闭流");
Console.WriteLine("所有生命周期断言通过");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

sealed class StreamSession(Stream stream, bool ownsStream) : IDisposable
{
    private bool _disposed;

    public int ReadFirstByte()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return stream.ReadByte();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (ownsStream) stream.Dispose();
    }
}

sealed class TrackingStream(byte[] data) : MemoryStream(data)
{
    public bool WasDisposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing) WasDisposed = true;
        base.Dispose(disposing);
    }
}
