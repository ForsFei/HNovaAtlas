using System.Net;
using System.Net.Http.Headers;

Uri address = new("https://example.invalid/readings");

var deltaClock = new ManualClock();
using (var handler = new SequenceHandler(
    new(HttpStatusCode.ServiceUnavailable, new(TimeSpan.FromSeconds(2))),
    new(HttpStatusCode.OK)))
using (var client = new HttpClient(handler, disposeHandler: false))
{
    var waits = new List<TimeSpan>();
    string body = await GetWithinBudgetAsync(client, address, deltaClock,
        (time, token) => AdvanceAsync(deltaClock, waits, time, token), CancellationToken.None);
    Check(body == "ok" && handler.Calls == 2 && waits.SequenceEqual([TimeSpan.FromSeconds(2)]),
        "503 delta is honored before the second send");
}

var dateClock = new ManualClock();
using (var handler = new SequenceHandler(
    new(HttpStatusCode.TooManyRequests,
        new RetryConditionHeaderValue(dateClock.GetUtcNow().AddSeconds(3))),
    new(HttpStatusCode.OK)))
using (var client = new HttpClient(handler, disposeHandler: false))
{
    var waits = new List<TimeSpan>();
    await GetWithinBudgetAsync(client, address, dateClock,
        (time, token) => AdvanceAsync(dateClock, waits, time, token), CancellationToken.None);
    Check(handler.Calls == 2 && waits.SequenceEqual([TimeSpan.FromSeconds(3)]),
        "429 HTTP-date is converted using the injected clock");
}

var shortClock = new ManualClock();
using (var handler = new SequenceHandler(
    new ResponsePlan(HttpStatusCode.ServiceUnavailable, new(TimeSpan.FromSeconds(8)))))
using (var client = new HttpClient(handler, disposeHandler: false))
{
    var waits = new List<TimeSpan>();
    await ExpectAsync<TimeoutException>(() => GetWithinBudgetAsync(client, address,
        shortClock, (time, token) => AdvanceAsync(shortClock, waits, time, token),
        CancellationToken.None));
    Check(handler.Calls == 1 && waits.Count == 0,
        "a hint beyond the remaining five-second budget is not shortened or sent");
}

var cancelClock = new ManualClock();
using (var handler = new SequenceHandler(
    new(HttpStatusCode.ServiceUnavailable, new(TimeSpan.FromSeconds(1))),
    new(HttpStatusCode.OK)))
using (var client = new HttpClient(handler, disposeHandler: false))
using (var stop = new CancellationTokenSource())
{
    await ExpectAsync<OperationCanceledException>(() => GetWithinBudgetAsync(client,
        address, cancelClock, (_, token) =>
        {
            stop.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }, stop.Token));
    Check(handler.Calls == 1, "cancellation during wait prevents the second send");
}

Console.WriteLine("Retry-After、剩余预算与等待取消断言全部通过。");

static async Task<string> GetWithinBudgetAsync(HttpClient client, Uri address,
    TimeProvider clock, Func<TimeSpan, CancellationToken, Task> delay,
    CancellationToken token)
{
    TimeSpan budget = TimeSpan.FromSeconds(5);
    long started = clock.GetTimestamp();
    for (int attempt = 1; attempt <= 3; attempt++)
    {
        token.ThrowIfCancellationRequested();
        if (clock.GetElapsedTime(started) >= budget) throw new TimeoutException("总预算已用尽");

        TimeSpan wait;
        using (var request = new HttpRequestMessage(HttpMethod.Get, address))
        using (HttpResponseMessage response = await client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, token))
        {
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadAsStringAsync(token);
            if (attempt == 3 || response.StatusCode is not
                (HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests))
                response.EnsureSuccessStatusCode();

            RetryConditionHeaderValue? hint = response.Headers.RetryAfter;
            wait = hint?.Delta ?? (hint?.Date - clock.GetUtcNow())
                ?? TimeSpan.FromSeconds(1);
            if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
        }

        TimeSpan remaining = budget - clock.GetElapsedTime(started);
        if (wait >= remaining) throw new TimeoutException("等待提示超出剩余预算");
        await delay(wait, token);
    }
    throw new InvalidOperationException("Unreachable retry state.");
}

static Task AdvanceAsync(ManualClock clock, List<TimeSpan> waits,
    TimeSpan time, CancellationToken token)
{
    token.ThrowIfCancellationRequested();
    waits.Add(time);
    clock.Advance(time);
    return Task.CompletedTask;
}

static async Task ExpectAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"预期 {typeof(T).Name}");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class ManualClock : TimeProvider
{
    private long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() =>
        new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(_ticks);
    public void Advance(TimeSpan time) => _ticks += time.Ticks;
}

sealed record ResponsePlan(HttpStatusCode Status, RetryConditionHeaderValue? RetryAfter = null);

sealed class SequenceHandler(params ResponsePlan[] plans) : HttpMessageHandler
{
    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Method != HttpMethod.Get) throw new InvalidOperationException("GET only");
        ResponsePlan plan = plans[Math.Min(Calls, plans.Length - 1)];
        Calls++;
        var response = new HttpResponseMessage(plan.Status)
        {
            Content = new StringContent("ok")
        };
        response.Headers.RetryAfter = plan.RetryAfter;
        return Task.FromResult(response);
    }
}
