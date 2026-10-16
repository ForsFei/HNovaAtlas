using System.Net;

Uri address = new("https://example.invalid/readings");

using (var handler = new SequenceHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK))
using (var client = new HttpClient(handler, disposeHandler: false))
{
    string body = await GetWithRetryAsync(client, address, 3, CancellationToken.None);
    Check(body == "ok" && handler.Calls == 2 && handler.DisposedContents == 2,
        "a temporary 503 is retried and both responses are released");
}

using (var handler = new SequenceHandler(HttpStatusCode.BadRequest, HttpStatusCode.OK))
using (var client = new HttpClient(handler, disposeHandler: false))
{
    await ExpectAsync<HttpRequestException>(() => GetWithRetryAsync(
        client, address, 3, CancellationToken.None));
    Check(handler.Calls == 1 && handler.DisposedContents == 1,
        "a 400 is not treated as transient");
}

using (var handler = new SequenceHandler(HttpStatusCode.ServiceUnavailable))
using (var client = new HttpClient(handler, disposeHandler: false))
{
    await ExpectAsync<HttpRequestException>(() => GetWithRetryAsync(
        client, address, 3, CancellationToken.None));
    Check(handler.Calls == 3 && handler.DisposedContents == 3,
        "maxAttempts includes the initial request and stops after three sends");
}

using (var handler = new SequenceHandler(HttpStatusCode.OK))
using (var client = new HttpClient(handler, disposeHandler: false))
using (var stop = new CancellationTokenSource())
{
    stop.Cancel();
    await ExpectAsync<OperationCanceledException>(() => GetWithRetryAsync(
        client, address, 3, stop.Token));
    Check(handler.Calls == 0, "cancellation prevents the next send");
}

Console.WriteLine("HTTP 有限重试、失败分类、取消与资源释放断言全部通过。");

static async Task<string> GetWithRetryAsync(HttpClient client, Uri address,
    int maxAttempts, CancellationToken cancellationToken)
{
    ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
    for (int attempt = 1; attempt <= maxAttempts; attempt++)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        using HttpResponseMessage response = await client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable && attempt < maxAttempts)
            continue;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
    throw new InvalidOperationException("Unreachable retry state.");
}

static async Task ExpectAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class SequenceHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public int DisposedContents { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Method != HttpMethod.Get) throw new InvalidOperationException("GET only");
        HttpStatusCode status = statuses[Math.Min(Calls, statuses.Length - 1)];
        Calls++;
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new TrackedContent(() => DisposedContents++)
        });
    }
}

sealed class TrackedContent(Action onDispose) : StringContent("ok")
{
    protected override void Dispose(bool disposing)
    {
        if (disposing) onDispose();
        base.Dispose(disposing);
    }
}
