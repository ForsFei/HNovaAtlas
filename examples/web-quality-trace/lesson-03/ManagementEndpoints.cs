using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

static class ManagementEndpoints
{
    public static bool ValidCode(string? code) => code is not null && Regex.IsMatch(code, "^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant);
    public static bool ValidPassword(string? password) => password is { Length: >= 12 and <= 256 } && !password.Any(char.IsControl);
    private static bool ValidRole(string? role) => role is "viewer" or "reviewer" or "admin";
    public static AccountSummary PublicAccount(TraceAccount a) => new(a.UserName, a.Role, a.Enabled, a.Version);

    public static void AddManagement(this WebApplicationBuilder builder, string database)
    {
        builder.Services.AddHealthChecks().AddCheck<ManagementReadiness>("management-db", tags: ["ready"]);
        builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("management-login", limiter =>
        {
            limiter.PermitLimit = 20; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0;
        }));
        builder.Services.AddDbContext<ManagementDb>(o => o.UseSqlite(new SqliteConnectionStringBuilder
            { DataSource = database, ForeignKeys = true }.ToString()));
        builder.Services.Configure<AntiforgeryOptions>(options => options.Cookie.SecurePolicy =
            builder.Configuration.GetValue<bool>("Management:SecureCookies") ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest);
        builder.Services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Cookie.SecurePolicy = builder.Configuration.GetValue<bool>("Management:SecureCookies") ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
            options.Events.OnValidatePrincipal = async context =>
            {
                var user = context.Principal?.Identity?.Name;
                var stamp = context.Principal?.FindFirst("accountVersion")?.Value;
                var db = context.HttpContext.RequestServices.GetRequiredService<ManagementDb>();
                var current = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.UserName == user);
                if (current is null || !current.Enabled || current.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) != stamp)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync();
                }
            };
        });
    }

    public static async Task<TraceAccount?> AuthenticateAsync(ManagementDb db, string? user, string? password)
    {
        if (!ValidCode(user) || password is null || password.Length > 256) return null;
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.UserName == user);
        if (account is null || !account.Enabled) return null;
        var hasher = new PasswordHasher<TraceAccount>();
        var result = hasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;
        // Rehash upgrades do not revoke otherwise-valid sessions.
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.PasswordHash = hasher.HashPassword(account, password);
            await db.SaveChangesAsync();
        }
        return account;
    }

    public static void MapManagement(this WebApplication app)
    {
        var group = app.MapGroup("/api/management").RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            http.Response.Headers.CacheControl = "no-store";
            if (http.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip))
                return Results.StatusCode(403);
            if (http.Request.Method is "POST" or "PUT" or "DELETE" or "PATCH")
            {
                try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
                catch (AntiforgeryValidationException) { return Results.Problem(statusCode: 400, title: "Invalid CSRF token"); }
            }
            return await next(context);
        });
        group.MapGet("/me", (HttpContext http) => Results.Ok(new ManagementIdentity(http.User.Identity!.Name!, http.User.FindFirst(ClaimTypes.Role)!.Value))).WithName("ManagementIdentity").Produces<ManagementIdentity>();
        group.MapGet("/station-codes", async (ManagementDb management, TraceDb trace) =>
        {
            var historical = await trace.Inspections.Select(i => i.Station).Distinct().ToListAsync();
            var configured = await management.Stations.Select(s => s.Code).ToListAsync();
            return Results.Ok(historical.Concat(configured).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList());
        }).WithName("ListKnownStationCodes").Produces<List<string>>();
        group.MapGet("/stations", async (ManagementDb db) => Results.Ok(await db.Stations.AsNoTracking().OrderBy(s => s.Code).ToListAsync())).WithName("ListStationProfiles").Produces<List<StationProfile>>();
        group.MapPost("/stations", async Task<IResult> (StationInput input, ManagementDb db, HttpContext http) =>
        {
            if (!ValidCode(input.Code) || !ValidName(input.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["station"] = ["编码须为小写字母、数字或连字符，长度1～64；名称去除首尾空白后为1～100字符。"] });
            var station = new StationProfile { Code = input.Code!, Name = input.Name!.Trim(), Enabled = true, Version = 1 };
            db.Stations.Add(station);
            db.Record(http.User.Identity!.Name!, "create-station", station.Code);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteErrorCode: 19 })
            { return Results.Problem(statusCode: 409, title: "Station code already exists"); }
            return Results.Created("/api/management/stations/" + station.Code, station);
        }).WithName("CreateStationProfile").Produces<StationProfile>(201).ProducesProblem(400).ProducesProblem(409).RequireAuthorization(p => p.RequireRole("admin"));
        group.MapPut("/stations/{code}", async Task<IResult> (string code, StationInput input, ManagementDb db, HttpContext http) =>
        {
            if (input.Code != code || !ValidName(input.Name) || input.Version is < 1 or long.MaxValue)
                return Results.Problem(statusCode: 400, title: "Code is immutable; provide a name and version");
            var station = await db.Stations.SingleOrDefaultAsync(s => s.Code == code);
            if (station is null) return Results.NotFound();
            if (station.Version != input.Version) return Conflict();
            station.Name = input.Name!.Trim(); station.Enabled = input.Enabled; station.Version++;
            db.Record(http.User.Identity!.Name!, "update-station", code);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException) { return Conflict(); }
            return Results.Ok(station);
        }).WithName("UpdateStationProfile").Produces<StationProfile>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409).RequireAuthorization(p => p.RequireRole("admin"));
        group.MapGet("/accounts", async (ManagementDb db) => Results.Ok(
            await db.Accounts.AsNoTracking().OrderBy(a => a.UserName).Select(a => new AccountSummary(a.UserName, a.Role, a.Enabled, a.Version)).ToListAsync())).WithName("ListTraceAccounts").Produces<List<AccountSummary>>()
            .RequireAuthorization(p => p.RequireRole("admin"));
        group.MapPost("/accounts", async Task<IResult> (AccountInput input, ManagementDb db, HttpContext http) =>
        {
            if (!ValidCode(input.UserName) || !ValidRole(input.Role) || !ValidPassword(input.Password))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["account"] = ["用户名须为1～64位小写编码；角色为viewer/reviewer/admin；密码为12～256字符。"] });
            var a = new TraceAccount { UserName = input.UserName!, Role = input.Role!, Enabled = true, Version = 1 };
            a.PasswordHash = new PasswordHasher<TraceAccount>().HashPassword(a, input.Password!);
            db.Accounts.Add(a); db.Record(http.User.Identity!.Name!, "create-account", a.UserName);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteErrorCode: 19 })
            { return Results.Problem(statusCode: 409, title: "User already exists"); }
            return Results.Created("/api/management/accounts/" + a.UserName, PublicAccount(a));
        }).WithName("CreateTraceAccount").Produces<AccountSummary>(201).ProducesProblem(400).ProducesProblem(409).RequireAuthorization(p => p.RequireRole("admin"));
        group.MapPut("/accounts/{user}", async Task<IResult> (string user, AccountInput input, ManagementDb db, HttpContext http) =>
        {
            if (input.UserName != user || !ValidRole(input.Role) || input.Version is < 1 or long.MaxValue ||
                (input.Password is { Length: > 0 } && !ValidPassword(input.Password)))
                return Results.Problem(statusCode: 400, title: "Invalid account update");
            await using var tx = await db.Database.BeginTransactionAsync();
            var a = await db.Accounts.SingleOrDefaultAsync(a => a.UserName == user);
            if (a is null) return Results.NotFound();
            if (a.Version != input.Version) return Conflict();
            if (a.Role == "admin" && a.Enabled && (!input.Enabled || input.Role != "admin") &&
                await db.Accounts.CountAsync(a => a.Role == "admin" && a.Enabled) <= 1)
                return Results.Problem(statusCode: 409, title: "Keep at least one enabled administrator");
            a.Role = input.Role!; a.Enabled = input.Enabled; a.Version++;
            if (!string.IsNullOrEmpty(input.Password)) a.PasswordHash = new PasswordHasher<TraceAccount>().HashPassword(a, input.Password);
            db.Record(http.User.Identity!.Name!, "update-account", user);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException) { return Conflict(); }
            await tx.CommitAsync();
            return Results.Ok(PublicAccount(a));
        }).WithName("UpdateTraceAccount").Produces<AccountSummary>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409).RequireAuthorization(p => p.RequireRole("admin"));
        group.MapGet("/audit", async (ManagementDb db) => Results.Ok(await db.Audit.AsNoTracking().OrderByDescending(a => a.Id).Take(100).ToListAsync())).WithName("ReadManagementAudit").Produces<List<ManagementAudit>>()
            .RequireAuthorization(p => p.RequireRole("admin"));
        group.MapGet("/statistics", StatisticsAsync).WithName("ReadQualityStatistics").Produces<QualityStatistics>().ProducesProblem(400);
    }

    private static bool ValidName(string? name) => name?.Trim() is { Length: >= 1 and <= 100 } text && !text.Any(char.IsControl);
    private static IResult Conflict() => Results.Problem(statusCode: 409, title: "Record changed; reload before saving");

    private static async Task<IResult> StatisticsAsync(string? station, string? since, string? until, TraceDb db)
    {
        if (!TimeRange.TryReadUtc(since, out var start) || !TimeRange.TryReadUtc(until, out var end) ||
            (start.HasValue && end.HasValue && start >= end) || (station?.Length ?? 0) > 64)
            return Results.Problem(statusCode: 400, title: "Invalid station or UTC range");
        var query = db.Inspections.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(station)) query = query.Where(i => i.Station == station);
        if (start.HasValue) query = query.Where(i => i.ImportedTicks >= start.Value);
        if (end.HasValue) query = query.Where(i => i.ImportedTicks < end.Value);
        var rows = await query.GroupBy(i => i.Station).Select(g => new {
            Station = g.Key, Total = g.Count(), Ng = g.Count(i => i.Result == "NG"),
            Pending = g.Count(i => i.Result == "NG" && !db.Reviews.Any(r => r.InspectionId == i.Id))
        }).OrderBy(g => g.Station).ToListAsync();
        int total = rows.Sum(r => r.Total), ng = rows.Sum(r => r.Ng);
        return Results.Ok(new QualityStatistics(total, ng, rows.Sum(r => r.Pending),
            total == 0 ? null : (double)ng / total,
            rows.Select(r => new StationStatistics(r.Station,r.Total,r.Ng,r.Pending)).ToList(),
            "ImportedAt UTC; inclusive since, exclusive until"));
    }

    private sealed record StationInput(string? Code, string? Name, bool Enabled, long Version);
    private sealed record AccountInput(string? UserName, string? Role, string? Password, bool Enabled, long Version);
}

sealed record AccountSummary(string UserName, string Role, bool Enabled, long Version);
sealed record ManagementIdentity(string User, string Role);
sealed record StationStatistics(string Station, int Total, int Ng, int Pending);
sealed record QualityStatistics(int Total, int Ng, int Pending, double? NgRate, List<StationStatistics> Stations, string TimeBasis);
