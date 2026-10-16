using System.Net;
using System.Net.Http;

Uri address = new("https://example.invalid/readings.bin");

using (HttpClient client = NewClient(() => Response([1, 2, 3, 4])))
using (MemoryStream target = new())
{
    long count = await DownloadAsync(client, address, target, 4, CancellationToken.None);
    Require(count == 4 && target.ToArray().SequenceEqual(new byte[] { 1, 2, 3, 4 }), "限额内下载成功");
    Require(target.CanWrite, "下载方法不拥有目标流");
}

using (HttpClient client = NewClient(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
using (MemoryStream target = new())
{
    await ExpectAsync<HttpRequestException>(async () =>
        await DownloadAsync(client, address, target, 4, CancellationToken.None));
    Require(target.Length == 0, "状态码失败时没有写入正文");
}

using (HttpClient client = NewClient(() => Response([1, 2, 3, 4, 5])))
using (MemoryStream target = new())
{
    await ExpectAsync<InvalidDataException>(async () =>
        await DownloadAsync(client, address, target, 4, CancellationToken.None));
    Require(target.Length == 0, "长度头超限时提前拒绝");
}

using (HttpClient client = NewClient(() => Response([1, 2, 3, 4, 5], knownLength: false)))
using (MemoryStream target = new())
{
    await ExpectAsync<InvalidDataException>(async () =>
        await DownloadAsync(client, address, target, 4, CancellationToken.None));
    Require(target.Length > 0 && target.Length <= 4, "未知长度仍受实际字节限制");
}

using (var source = new CancellationTokenSource())
using (HttpClient client = NewClient(() => new HttpResponseMessage(HttpStatusCode.OK)
{
    Content = new StreamContent(new CancelOnFirstReadStream([1, 2, 3, 4], source))
}))
using (MemoryStream target = new())
{
    await ExpectAsync<OperationCanceledException>(async () =>
        await DownloadAsync(client, address, target, 10, source.Token));
    Require(source.IsCancellationRequested, "正文读取时触发取消");
}

Console.WriteLine("HTTP 成功、状态码、大小上限与取消断言通过。");

static async Task<long> DownloadAsync(HttpClient client, Uri address, Stream destination,
    long maxBytes, CancellationToken cancellationToken)
{
    ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
    using HttpResponseMessage response = await client.GetAsync(
        address, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    response.EnsureSuccessStatusCode();

    if (response.Content.Headers.ContentLength is long declared && declared > maxBytes)
        throw new InvalidDataException("响应声明长度超出上限");

    await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
    byte[] buffer = new byte[3]; // 教学用小缓冲，方便观察分块；实际应用可调大
    long total = 0;
    int read;
    while ((read = await body.ReadAsync(buffer, cancellationToken)) != 0)
    {
        if (read > maxBytes - total)
            throw new InvalidDataException("实际响应体超出上限");
        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        total += read;
    }
    return total;
}

static HttpClient NewClient(Func<HttpResponseMessage> factory) => new(new StubHandler(factory));

static HttpResponseMessage Response(byte[] bytes, bool knownLength = true)
{
    var response = new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StreamContent(new MemoryStream(bytes))
    };
    if (!knownLength) response.Content.Headers.ContentLength = null;
    return response;
}

static async Task ExpectAsync<TException>(Func<Task> action) where TException : Exception
{
    try { await action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"未观察到预期异常：{typeof(TException).Name}");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

public sealed class StubHandler(Func<HttpResponseMessage> factory) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(factory());
}

public sealed class CancelOnFirstReadStream(byte[] bytes, CancellationTokenSource source)
    : MemoryStream(bytes)
{
    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await base.ReadAsync(buffer, cancellationToken);
        if (read > 0) source.Cancel();
        return read;
    }
}
