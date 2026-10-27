using System.IO.Ports;
using System.Text;
using IndustrialSerialPort;

namespace SerialFraming;

// The small boundary lets offline checks exercise the real session algorithm.
public interface IBytePort : IDisposable
{
    void Open();
    int Read(byte[] buffer);
    void WriteLine(string text);
}

public sealed class SerialBytePort(string portName, int baudRate) : IBytePort
{
    private readonly SerialPort _port = DemoPort.Create(portName, baudRate);

    public void Open()
    {
        _port.ReadTimeout = 100;
        _port.WriteTimeout = 500;
        _port.Open();
    }

    public int Read(byte[] buffer) => _port.Read(buffer, 0, buffer.Length);
    public void WriteLine(string text) => _port.WriteLine(text);
    // Dispose closes an open port as well; there is no concurrent UI-side Close.
    public void Dispose() => _port.Dispose();
}

// In-memory teaching device, NOT a virtual COM port or driver test.
public sealed class DemoBytePort : IBytePort
{
    private readonly Queue<byte[]> _chunks = new();
    private bool _open;

    public void Open() => _open = true;

    public void WriteLine(string text)
    {
        EnsureOpen();
        if (text != TemperatureProtocol.Request) throw new FormatException("未知教学请求。");
        foreach (string part in new[] { "TE", "MP=2", "53\r", "\n" })
            _chunks.Enqueue(Encoding.ASCII.GetBytes(part));
    }

    public int Read(byte[] buffer)
    {
        EnsureOpen();
        Thread.Sleep(25); // Models a finite blocking read on the background worker.
        if (!_chunks.TryDequeue(out byte[]? chunk)) throw new TimeoutException();
        chunk.CopyTo(buffer, 0);
        return chunk.Length;
    }

    public void Dispose() { _open = false; _chunks.Clear(); }

    private void EnsureOpen()
    {
        if (!_open) throw new InvalidOperationException("教学端口尚未打开。");
    }
}
