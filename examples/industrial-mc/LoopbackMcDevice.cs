using System.Net;
using System.Net.Sockets;

namespace IndustrialMc;

internal enum McFault { None, SplitReply, Reject, Silent, Close, BadHeader, ShortRead, ApplyThenDrop }

// Independent subset, not GX Simulator. One client at a time; no real outputs.
internal sealed class LoopbackMcDevice : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serve;
    private readonly McFault _fault;
    private readonly ushort[] _d = new ushort[256];
    private readonly ushort[] _w = new ushort[256];
    private readonly bool[] _m = new bool[256];
    private readonly bool[] _x = new bool[256];
    private readonly object _dataLock = new();
    private int _requests, _writes;
    private byte[]? _lastRequest;
    public IPEndPoint Endpoint { get; }
    public int Requests => Volatile.Read(ref _requests);
    public int Writes => Volatile.Read(ref _writes);
    public byte[]? LastRequest => Volatile.Read(ref _lastRequest);
    public TaskCompletionSource RequestReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public LoopbackMcDevice(McFault fault = McFault.None)
    {
        _fault = fault;
        _d[100] = unchecked((ushort)-123);
        _d[101] = 0x5678; _d[102] = 0x1234;
        _d[103] = 0; _d[104] = 0x41BC; // IEEE Single 23.5, low word first.
        _d[105] = ushort.MaxValue;
        _d[110] = 250;
        _w[0x10] = 1111; _w[0x16] = 2222;
        _m[100] = true; _m[102] = true; _m[104] = true;
        _x[0x10] = true;
        _listener.Start();
        Endpoint = (IPEndPoint)_listener.LocalEndpoint;
        _serve = ServeAsync();
    }

    public ushort InspectD(int index) { lock (_dataLock) return _d[index]; }

    private async Task ServeAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                try { await HandleAsync(client.GetStream()); }
                catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException) { }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { _listener.Stop(); }
    }

    private async Task HandleAsync(NetworkStream stream)
    {
        while (true)
        {
            byte[] header = new byte[9];
            int first = await stream.ReadAsync(header.AsMemory(0, 1), _stop.Token);
            if (first == 0) return;
            await stream.ReadExactlyAsync(header.AsMemory(1), _stop.Token);
            byte[] body = new byte[McFrames.RequestLength(header)];
            await stream.ReadExactlyAsync(body, _stop.Token);
            Volatile.Write(ref _lastRequest, [.. header, .. body]);
            Interlocked.Increment(ref _requests);
            RequestReceived.TrySetResult();
            if (_fault == McFault.Close) return;
            if (_fault == McFault.Silent) continue;
            byte[] response = _fault == McFault.Reject
                ? McFrames.Response(0xC059, []) : Apply(body);
            if (_fault == McFault.ApplyThenDrop && McFrames.U16(body.AsSpan(2)) == 0x1401) return;
            if (_fault == McFault.BadHeader) response[0] = 0xD4;
            if (_fault == McFault.ShortRead && response.Length >= 15)
                response = McFrames.Response(0, response[11..^2]); // Well-framed, but fewer values than requested.
            if (_fault == McFault.SplitReply)
            {
                foreach (byte value in response)
                {
                    await stream.WriteAsync(new byte[] { value }, _stop.Token);
                    await Task.Delay(2, _stop.Token);
                }
            }
            else await stream.WriteAsync(response, _stop.Token);
        }
    }

    private byte[] Apply(byte[] body)
    {
        lock (_dataLock)
        {
            ushort command = McFrames.U16(body.AsSpan(2));
            ushort sub = McFrames.U16(body.AsSpan(4));
            int start = body[6] | body[7] << 8 | body[8] << 16;
            byte code = body[9];
            int count = McFrames.U16(body.AsSpan(10));
            byte[] Reject() => McFrames.Response(0xC059, []); // Teaching refusal; not a model-wide error-code emulator.
            if (command is not (0x0401 or 0x1401) || sub > 1 || count is < 1 or > 64 || start > 255 || start + count > 256)
                return Reject();
            bool writing = command == 0x1401;
            int dataLength = sub == 0 ? count * 2 : (count + 1) / 2;
            if (body.Length != 12 + (writing ? dataLength : 0)) return Reject();
            if (sub == 0 && code is 0xA8 or 0xB4)
            {
                ushort[] words = code == 0xA8 ? _d : _w;
                if (!writing)
                {
                    byte[] data = new byte[count * 2];
                    for (int i = 0; i < count; i++) McFrames.Put16(data.AsSpan(i * 2), words[start + i]);
                    return McFrames.Response(0, data);
                }
                // All validation precedes modification. Only local scratch D120..127 or constrained D110.
                bool target = start == 110 && count == 1;
                if (code != 0xA8 || (!target && (start < 120 || start + count > 128))) return Reject();
                if (target && McFrames.U16(body.AsSpan(12)) > 1000) return Reject();
                for (int i = 0; i < count; i++) words[start + i] = McFrames.U16(body.AsSpan(12 + i * 2));
            }
            else if (sub == 1 && code is 0x90 or 0x9C)
            {
                bool[] bits = code == 0x90 ? _m : _x;
                if (!writing) return McFrames.Response(0, McFrames.PackBits(bits.AsSpan(start, count)));
                if (code != 0x90 || start < 100 || start + count > 105) return Reject();
                bool[] values = McFrames.UnpackBits(body.AsSpan(12), count);
                values.CopyTo(bits, start);
            }
            else return Reject();
            Interlocked.Increment(ref _writes);
            return McFrames.Response(0, []);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try { await _serve; }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
        finally { _stop.Dispose(); }
    }
}
