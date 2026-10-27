using System.Net;
using System.Net.Sockets;
using IndustrialNetworking;
using SerialFraming;

namespace IndustrialTcpSession;

public enum DemoScenario { Normal, DropOnce, IdleCloseOnce, PartialOnce, SilentPing, SilentTemperature, StaleReplies, DuplicateReplies, Malformed, WrongKind }

public sealed class LoopbackDevice : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serve;
    private readonly DemoScenario _scenario;
    private int _connections, _requests, _pings, _temperatures;
    private string? _lastSessionError;
    public IPEndPoint Endpoint { get; }
    public int Connections => Volatile.Read(ref _connections);
    public int Requests => Volatile.Read(ref _requests);
    public int Pings => Volatile.Read(ref _pings);
    public int Temperatures => Volatile.Read(ref _temperatures);
    public string? LastSessionError => Volatile.Read(ref _lastSessionError);

    public LoopbackDevice(DemoScenario scenario = DemoScenario.Normal)
    {
        _scenario = scenario;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Endpoint = (IPEndPoint)_listener.LocalEndpoint;
        _serve = ServeAsync();
    }

    private async Task ServeAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using TcpClient peer = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                int sessionNumber = Interlocked.Increment(ref _connections);
                try { await ServeConnectionAsync(peer, sessionNumber).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or SocketException or FormatException)
                { Volatile.Write(ref _lastSessionError, $"{error.GetType().Name}: {error.Message}"); }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { _listener.Stop(); }
    }

    private async Task ServeConnectionAsync(TcpClient peer, int sessionNumber)
    {
        using NetworkStream stream = peer.GetStream();
        var decoder = new CrlfFrameDecoder(WireProtocol.MaxPayload);
        byte[] buffer = new byte[256];
        int inSession = 0;
        while (true)
        {
            int count = await stream.ReadAsync(buffer.AsMemory(), _stop.Token).ConfigureAwait(false);
            if (count == 0) return;
            foreach (string frame in decoder.Feed(buffer.AsSpan(0, count)))
            {
                (RequestKind kind, string id) = SessionProtocol.ParseRequest(frame);
                Interlocked.Increment(ref _requests);
                if (kind == RequestKind.Ping) Interlocked.Increment(ref _pings);
                else Interlocked.Increment(ref _temperatures);
                inSession++;
                if (_scenario == DemoScenario.DropOnce && sessionNumber == 1 && inSession == 3) return;
                if (_scenario == DemoScenario.PartialOnce && sessionNumber == 1)
                {
                    await stream.WriteAsync("PONG "u8.ToArray(), _stop.Token).ConfigureAwait(false);
                    return;
                }
                if ((_scenario == DemoScenario.SilentPing && kind == RequestKind.Ping)
                    || (_scenario == DemoScenario.SilentTemperature && kind == RequestKind.Temperature)) continue;
                if (_scenario == DemoScenario.StaleReplies)
                    await stream.WriteAsync(SessionProtocol.Response(kind, WireProtocol.NewId()), _stop.Token).ConfigureAwait(false);
                byte[] response = _scenario switch
                {
                    DemoScenario.Malformed => WireProtocol.Encode("BROKEN"),
                    DemoScenario.WrongKind => SessionProtocol.Response(kind == RequestKind.Ping ? RequestKind.Temperature : RequestKind.Ping, id),
                    _ => SessionProtocol.Response(kind, id),
                };
                if (_scenario == DemoScenario.DuplicateReplies) response = [.. response, .. response];
                await stream.WriteAsync(response, _stop.Token).ConfigureAwait(false);
                if (_scenario == DemoScenario.IdleCloseOnce && sessionNumber == 1 && kind == RequestKind.Temperature)
                {
                    await Task.Delay(100, _stop.Token).ConfigureAwait(false);
                    return;
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { await _serve.ConfigureAwait(false); }
        finally { _listener.Stop(); _stop.Dispose(); }
    }
}
