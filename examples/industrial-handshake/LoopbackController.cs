using System.Net;
using System.Net.Sockets;
using IndustrialMc;

namespace IndustrialHandshake;

internal sealed class LoopbackController : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serve, _scan;
    private int _submitWrites, _releaseWrites, _connections;
    public Mailbox Model { get; } = new();
    public IPEndPoint Endpoint { get; }
    public bool DropNextSubmitReply { get; set; }
    public bool DropNextReleaseReply { get; set; }
    public bool TornNextStatus { get; set; }
    public int SubmitWrites => Volatile.Read(ref _submitWrites);
    public int ReleaseWrites => Volatile.Read(ref _releaseWrites);
    public int Connections => Volatile.Read(ref _connections);

    public LoopbackController()
    {
        _listener.Start(); Endpoint = (IPEndPoint)_listener.LocalEndpoint;
        _serve = ServeAsync(); _scan = ScanAsync();
    }

    private async Task ScanAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try { while (await timer.WaitForNextTickAsync(_stop.Token)) Model.Advance(); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task ServeAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                Interlocked.Increment(ref _connections);
                try { await HandleAsync(client.GetStream()); }
                catch (Exception error) when (error is IOException or SocketException or InvalidDataException) { }
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
            if (await stream.ReadAsync(header.AsMemory(0, 1), _stop.Token) == 0) return;
            await stream.ReadExactlyAsync(header.AsMemory(1), _stop.Token);
            byte[] body = new byte[McFrames.RequestLength(header)];
            await stream.ReadExactlyAsync(body, _stop.Token);
            ushort command = McFrames.U16(body.AsSpan(2));
            int start = body[6] | body[7] << 8 | body[8] << 16;
            int count = McFrames.U16(body.AsSpan(10));
            byte[] response = McFrames.Response(0xC059, []);
            bool drop = false;
            if (McFrames.U16(body.AsSpan(4)) == 0 && body[9] == 0xA8)
            {
                if (command == 0x0401 && body.Length == 12 && start == 240 && count == 18)
                {
                    ushort[] status = Model.Read().Encode();
                    if (TornNextStatus) { TornNextStatus = false; status[17] ^= 2; }
                    byte[] data = new byte[36];
                    for (int i = 0; i < status.Length; i++) McFrames.Put16(data.AsSpan(i * 2), status[i]);
                    response = McFrames.Response(0, data);
                }
                else if (command == 0x1401 && count is >= 1 and <= 6 && body.Length == 12 + 2 * count)
                {
                    ushort[] values = new ushort[count];
                    for (int i = 0; i < count; i++) values[i] = McFrames.U16(body.AsSpan(12 + 2 * i));
                    try
                    {
                        if (start >= 200 && start + count <= 206) Model.Stage(start - 200, values);
                        else if (start == 206 && count == 1)
                        {
                            Model.SetControl(values[0]);
                            if (values[0] == 1)
                            {
                                Interlocked.Increment(ref _submitWrites);
                                if (DropNextSubmitReply) { DropNextSubmitReply = false; drop = true; }
                            }
                            if (values[0] == 2)
                            {
                                Interlocked.Increment(ref _releaseWrites);
                                if (DropNextReleaseReply) { DropNextReleaseReply = false; drop = true; }
                            }
                        }
                        else throw new InvalidOperationException("Only staged fields or one control word may be written.");
                        response = McFrames.Response(0, []);
                    }
                    catch (Exception error) when (error is InvalidOperationException or ArgumentException) { }
                }
            }
            if (drop) return; // Business state changed BEFORE losing the MC write response.
            await stream.WriteAsync(response, _stop.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Stop();
        try { await Task.WhenAll(_serve, _scan); }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
        finally { _stop.Dispose(); }
    }
}
