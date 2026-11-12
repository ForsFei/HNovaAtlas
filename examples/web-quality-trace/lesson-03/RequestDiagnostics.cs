using System.Diagnostics;

static class RequestDiagnostics
{
    public static void UseRequestDiagnostics(this WebApplication app)
    {
        app.Use(async (http, next) =>
        {
            string requestId = Guid.NewGuid().ToString("N");
            http.TraceIdentifier = requestId;
            http.Response.OnStarting(() =>
            {
                http.Response.Headers["X-Request-Id"] = requestId;
                return Task.CompletedTask;
            });
            using var scope = app.Logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = requestId });
            long started = Stopwatch.GetTimestamp();
            try { await next(http); }
            finally
            {
                app.Logger.LogInformation("Request {RequestId} {Method} {Path} returned {Status} in {ElapsedMs} ms",
                    requestId, http.Request.Method, http.Request.Path, http.Response.StatusCode,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        });
        app.UseExceptionHandler();
        app.UseStatusCodePages();
    }
}
