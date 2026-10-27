using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using IndustrialNetworking;
using SerialFraming;

namespace IndustrialTcpSession;

// Owns an already connected client. The caller must finish requests before disposal.
public sealed class TcpConnection : IAsyncDisposable
{
    private sealed record Pending(string Id, RequestKind Kind, TaskCompletionSource<SessionReply> Source);
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly CancellationTokenSource _life = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly object _gate = new();
    private Pending? _pending;
    private Exception? _failure;
    private int _ignored;
    public Task Completion { get; }
    public int IgnoredReplies => Volatile.Read(ref _ignored);

    public TcpConnection(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
        Completion = ReceiveAsync();
    }

    public async Task<SessionReply> RequestAsync(RequestKind kind, TimeSpan timeout, CancellationToken stop)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        await _sendGate.WaitAsync(stop).ConfigureAwait(false);
        Pending? pending = null;
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(stop, _life.Token);
            budget.CancelAfter(timeout);
            pending = new(WireProtocol.NewId(), kind,
                new TaskCompletionSource<SessionReply>(TaskCreationOptions.RunContinuationsAsynchronously));
            lock (_gate)
            {
                if (_failure is not null) ExceptionDispatchInfo.Capture(_failure).Throw();
                _pending = pending; // Register before sending: a fast reply must not race registration.
            }
            try
            {
                await _stream.WriteAsync(SessionProtocol.Request(kind, pending.Id), budget.Token).ConfigureAwait(false);
                // This waits for an in-memory response promise, not an abandoned socket read.
                return await pending.Source.Task.WaitAsync(budget.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!stop.IsCancellationRequested && budget.IsCancellationRequested)
            { throw new TimeoutException($"{kind} 请求超时，放弃本次连接。"); }
        }
        catch
        {
            Abort(); // Never reuse a timed-out/partially written session.
            throw;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_pending, pending)) _pending = null;
                if (pending is not null)
                {
                    pending.Source.TrySetCanceled();
                    if (pending.Source.Task.IsFaulted) _ = pending.Source.Task.Exception;
                }
            }
            _sendGate.Release();
        }
    }

    private async Task ReceiveAsync()
    {
        var decoder = new CrlfFrameDecoder(WireProtocol.MaxPayload);
        byte[] buffer = new byte[256];
        try
        {
            while (true)
            {
                int count = await _stream.ReadAsync(buffer.AsMemory(), _life.Token).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException($"连接 EOF；残留字节 {decoder.BufferedBytes}。");
                foreach (string frame in decoder.Feed(buffer.AsSpan(0, count)))
                {
                    SessionReply reply = SessionProtocol.ParseResponse(frame);
                    lock (_gate)
                    {
                        if (_pending is not Pending pending || pending.Id != reply.Id || pending.Source.Task.IsCompleted)
                        {
                            Interlocked.Increment(ref _ignored);
                            continue;
                        }
                        if (pending.Kind != reply.Kind) throw new FormatException("响应编号正确但类型不匹配。");
                        pending.Source.TrySetResult(reply);
                    }
                }
            }
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                _failure = error;
                // A successful TrySetResult cannot be replaced by a later EOF exception.
                _pending?.Source.TrySetException(error);
            }
            throw;
        }
    }

    private void Abort()
    {
        _life.Cancel();
        _client.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Abort();
        try { await Completion.ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or SocketException or FormatException
            or OperationCanceledException or ObjectDisposedException) { /* Observed; the supervisor owns reporting. */ }
        finally { _stream.Dispose(); _life.Dispose(); _sendGate.Dispose(); }
    }
}
