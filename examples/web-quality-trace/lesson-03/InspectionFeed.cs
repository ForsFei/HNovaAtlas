using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

sealed class InspectionHub : Hub;

sealed class InspectionFeed(IServiceScopeFactory scopes, IHubContext<InspectionHub> hub,
    ILogger<InspectionFeed> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int? previousCount = null;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TraceDb>();
                int count = await db.Inspections.CountAsync(stoppingToken);
                if (count != previousCount)
                {
                    await hub.Clients.All.SendAsync("InspectionsChanged", stoppingToken);
                    previousCount = count;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Unable to check or notify inspection changes.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
