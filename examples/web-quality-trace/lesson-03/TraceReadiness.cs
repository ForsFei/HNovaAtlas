using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

sealed class TraceReadiness(TraceDb db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        db.Database.SetCommandTimeout(2);
        await db.Inspections.AsNoTracking().Select(TraceDb.Summary).Take(1).ToListAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
