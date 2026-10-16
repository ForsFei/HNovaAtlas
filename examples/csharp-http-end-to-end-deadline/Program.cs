using System.Net;

Uri address = new("https://example.invalid/data");

using (var handler = new BlockingHeaderHandler())
using (var client = NewClient(handler))
using (var caller = new CancellationTokenSource())
{
    Task<int> pending = ReadOneByteAsync(client, address, TimeSpan.FromSeconds(5), caller.Token);
    await handler.Entered.Task;
    caller.Cancel();
    await ExpectCanceledAsync(pending);
    Check(handler.Canceled && handler.Calls == 1, "caller cancellation reached SendAsync");
}

using (var handler = new BlockingHeaderHandler())
using (var client = NewClient(handler))
{
    Task<int> pending = ReadOneByteAsync(client, address, TimeSpan.FromMilliseconds(50),
        CancellationToken.None);
    await handler.Entered.Task;
    await ExpectCanceledAsync(pending);
    Check(handler.Canceled && handler.Calls == 1, "deadline canceled an in-flight send");
}

using (var body = new BlockingBodyStream())
using (var handler = new BodyHandler(body))
using (var client = NewClient(handler))
using (var caller = new CancellationTokenSource())
{
    Task<int> pending = ReadOneByteAsync(client, address, TimeSpan.FromSeconds(5), caller.Token);
    await body.Entered.Task;
    caller.Cancel();
    await ExpectCanceledAsync(pending);
    Check(body.Canceled && handler.Calls == 1, "caller cancellation reached body ReadAsync");
}

using (var body = new BlockingBodyStream())
using (var handler = new BodyHandler(body))
using (var client = NewClient(handler))
{
    Task<int> pending = ReadOneByteAsync(client, address, TimeSpan.FromMilliseconds(50),
        CancellationToken.None);
    await body.Entered.Task;
    await ExpectCanceledAsync(pending);
    Check(body.Canceled && handler.Calls == 1, "same deadline covered body ReadAsync");
}

Console.WriteLine("发送截止时间、调用方取消和正文读取取消均已验证。");

static HttpClient NewClient(HttpMessageHandler handler) =>
    new(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };

static async Task<int> ReadOneByteAsync(HttpClient client, Uri address, TimeSpan budget,
    CancellationToken callerToken)
{
    if (budget <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(budget));
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
    deadline.CancelAfter(budget);
    using var request = new HttpRequestMessage(HttpMethod.Get, address);
    using var response = await client.SendAsync(request,
        HttpCompletionOption.ResponseHeadersRead, deadline.Token);
    response.EnsureSuccessStatusCode();
    Stream body = await response.Content.ReadAsStreamAsync(deadline.Token);
    byte[] buffer = new byte[1];
    return await body.ReadAsync(buffer, deadline.Token);
}

static async Task ExpectCanceledAsync(Task task)
{
    try { await task; }
    catch (OperationCanceledException) { return; }
    throw new Exception("Expected cancellation");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

sealed class BlockingHeaderHandler : HttpMessageHandler
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Calls { get; private set; }
    public bool Canceled { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Calls++;
        Entered.TrySetResult();
        try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Canceled = true;
            throw;
        }
        throw new Exception("Unreachable");
    }
}

sealed class BodyHandler(BlockingBodyStream body) : HttpMessageHandler
{
    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(body)
        });
    }
}

sealed class BlockingBodyStream : Stream
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Canceled { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        Entered.TrySetResult();
        try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Canceled = true;
            throw;
        }
        throw new Exception("Unreachable");
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
        CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
