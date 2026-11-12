using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
bool managementEnabled = builder.Configuration.GetValue<bool>("Management:Enabled");
if (managementEnabled) builder.Configuration["Review:Enabled"] = "true";
if (new[] { "Management:Migrate", "Management:Bootstrap", "Management:RevokeSessions" }.Count(key => builder.Configuration.GetValue<bool>(key)) > 1)
    throw new ArgumentException("Run one management maintenance operation at a time.");
if (!managementEnabled && (builder.Configuration.GetValue<bool>("Management:Migrate") || builder.Configuration.GetValue<bool>("Management:Bootstrap") || builder.Configuration.GetValue<bool>("Management:RevokeSessions")))
    throw new ArgumentException("Management initialization requires Management:Enabled true.");
if ((builder.Configuration.GetValue<bool>("Management:Migrate") || builder.Configuration.GetValue<bool>("Management:Bootstrap") || builder.Configuration.GetValue<bool>("Management:RevokeSessions")) &&
    (builder.Configuration["Import"] is not null || builder.Configuration["Backup"] is not null || builder.Configuration.GetValue<bool>("Migrate")))
    throw new ArgumentException("Run management initialization separately from import, backup and trace migration.");
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
if (managementEnabled)
{
    builder.AddManagement(database);
    if (builder.Configuration["Management:KeyDirectory"] is { } keyDirectory)
    {
        if (!Path.IsPathFullyQualified(keyDirectory)) throw new ArgumentException("Management key directory must be absolute.");
        builder.Services.AddDataProtection().SetApplicationName("HNovaQualityTrace").PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
    }
}
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
if (managementEnabled)
{
    // Trust scheme information from the same-machine TLS proxy only; keep the peer IP loopback.
    var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedProto, ForwardLimit = 1 };
    forwarded.KnownProxies.Add(IPAddress.Loopback);
    app.UseForwardedHeaders(forwarded);
}
app.UseRequestDiagnostics();
if (reviewEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

if (managementEnabled)
{
    app.Use(async (http, next) =>
    {
        if (http.Request.Path.StartsWithSegments("/api") && !http.Request.Path.StartsWithSegments("/api/session") &&
            http.User.Identity?.IsAuthenticated != true)
        { await http.ChallengeAsync(); return; }
        await next(http);
    });
}
if (readLimitEnabled || managementEnabled) app.UseRateLimiter();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TraceDb>();
    await db.Database.EnsureCreatedAsync();
    if (reviewEnabled) await ReviewEndpoints.CreateReviewTableAsync(db);
    if (eventsEnabled) await InspectionEvents.InitializeAsync(db);
    if (managementEnabled)
    {
        var management = scope.ServiceProvider.GetRequiredService<ManagementDb>();
        if (builder.Configuration.GetValue<bool>("Management:Migrate"))
        {
            await management.MigrateAsync();
            Console.WriteLine("Management schema at version 1.");
            return;
        }
        await management.CheckSchemaAsync();
        if (builder.Configuration.GetValue<bool>("Management:RevokeSessions"))
        {
            await using var transaction = await management.Database.BeginTransactionAsync();
            if (await management.Accounts.AnyAsync(a => a.Version == long.MaxValue)) throw new InvalidDataException("Account version exhausted.");
            int count = await management.Accounts.ExecuteUpdateAsync(update => update.SetProperty(a => a.Version, a => a.Version + 1));
            management.Record("maintenance", "revoke-all-sessions", count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await management.SaveChangesAsync();
            await transaction.CommitAsync();
            Console.WriteLine($"Revoked existing sessions for {count} accounts.");
            return;
        }
        if (builder.Configuration.GetValue<bool>("Management:Bootstrap"))
        {
            await management.BootstrapAsync(builder.Configuration["Management:User"], builder.Configuration["Management:Password"]);
            Console.WriteLine("Initial administrator created.");
            return;
        }
    }

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
if (managementEnabled) app.MapManagement();
if (eventsEnabled) app.MapInspectionEvents();

if (contractEnabled) app.MapQueryContract();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

await app.RunAsync();
