using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
string database = builder.Configuration["TraceDb"]
    ?? Path.Combine(builder.Environment.ContentRootPath, ".local", "trace.db");
database = Path.GetFullPath(database);
if (builder.Configuration.GetValue<bool>("Migrate"))
{
    if (builder.Configuration["Import"] is not null || builder.Configuration["Backup"] is not null)
        throw new ArgumentException("Run migration separately from import and backup.");
    Console.WriteLine(TraceMigration.Apply(database) ? "Trace database upgraded to version 1." : "Trace database is already at version 1.");
    return;
}

if (builder.Configuration["Backup"] is { } backup)
{
    if (builder.Configuration["Import"] is not null)
        throw new ArgumentException("Run backup and import as separate commands.");
    Console.WriteLine($"Backup created: {TraceBackup.Create(database, backup)}");
    return;
}

Directory.CreateDirectory(Path.GetDirectoryName(database)!);

builder.Services.AddDbContext<TraceDb>(options =>
    options.UseSqlite(new SqliteConnectionStringBuilder { DataSource = database, ForeignKeys = true }.ToString()));
bool reviewEnabled = builder.Configuration.GetValue<bool>("Review:Enabled");
if (reviewEnabled) builder.Services.AddReviewServices();
bool eventsEnabled = builder.Configuration.GetValue<bool>("Events:Enabled");
bool liveEnabled = builder.Configuration.GetValue<bool>("Live:Enabled");
if (liveEnabled)
{
    builder.Services.AddSignalR();
    builder.Services.AddHostedService<InspectionFeed>();
}
builder.Services.AddHealthChecks().AddCheck<TraceReadiness>("trace-db", tags: ["ready"]);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["requestId"] = context.HttpContext.TraceIdentifier;
});
bool contractEnabled = builder.Configuration.GetValue<bool>("Contract:Enabled");
if (contractEnabled) builder.Services.AddQueryContract();
bool readLimitEnabled = builder.Configuration.GetValue<bool>("ReadLimit:Enabled");
if (readLimitEnabled) ReadRateLimit.AddServices(builder.Services, builder.Configuration);
var app = builder.Build();
app.UseRequestDiagnostics();
if (reviewEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

if (readLimitEnabled) app.UseRateLimiter();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TraceDb>();
    await db.Database.EnsureCreatedAsync();
    if (reviewEnabled) await ReviewEndpoints.CreateReviewTableAsync(db);
    if (eventsEnabled) await InspectionEvents.InitializeAsync(db);

    if (builder.Configuration["Import"] is { } folder)
    {
        var imported = await EvidenceImporter.ImportAsync(
            db, folder, builder.Configuration["Station"] ?? "station-a");
        Console.WriteLine(imported.Created ? $"Imported {imported.Id}" : $"Already imported {imported.Id}");
        return;
    }
}

if (liveEnabled)
{
    app.MapHub<InspectionHub>("/api/live");
    if (builder.Configuration["Live:Assets"] is { } assets)
    {
        string root = Path.GetFullPath(assets);
        if (!File.Exists(Path.Combine(root, "index.html")))
            throw new InvalidOperationException("Live:Assets must point to the built React directory.");
        var files = new PhysicalFileProvider(root);
        app.Lifetime.ApplicationStopped.Register(files.Dispose);
        app.UseFileServer(new FileServerOptions { FileProvider = files });
    }
}

app.MapInspectionEndpoints(reviewEnabled, readLimitEnabled);
app.MapInspectionPages(readLimitEnabled);
if (reviewEnabled) app.MapReviewEndpoints();
if (eventsEnabled) app.MapInspectionEvents();

if (contractEnabled) app.MapQueryContract();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

await app.RunAsync();
