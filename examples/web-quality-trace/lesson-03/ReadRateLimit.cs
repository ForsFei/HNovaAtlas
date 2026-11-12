using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

static class ReadRateLimit
{
    public const string Policy = "inspection-reads";

    public static void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        int permits = configuration.GetValue("ReadLimit:PermitLimit", 20);
        int seconds = configuration.GetValue("ReadLimit:WindowSeconds", 10);
        if (permits is < 1 or > 10000 || seconds is < 1 or > 3600)
            throw new ArgumentException("ReadLimit requires PermitLimit in 1..10000 and WindowSeconds in 1..3600.");

        services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter(Policy, limiter =>
            {
                limiter.PermitLimit = permits;
                limiter.Window = TimeSpan.FromSeconds(seconds);
                limiter.QueueLimit = 0;
                limiter.AutoReplenishment = true;
            });
            options.OnRejected = async (context, ct) =>
            {
                var http = context.HttpContext;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds))
                        .ToString(CultureInfo.InvariantCulture);
                http.Response.Headers.CacheControl = "no-store";
                await Results.Problem(statusCode: 429, title: "Inspection query limit reached").ExecuteAsync(http);
            };
        });
    }
}
