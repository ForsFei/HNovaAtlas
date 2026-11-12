using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;

sealed class ManagementDb(DbContextOptions<ManagementDb> options) : DbContext(options)
{
    public DbSet<StationProfile> Stations => Set<StationProfile>();
    public DbSet<TraceAccount> Accounts => Set<TraceAccount>();
    public DbSet<ManagementAudit> Audit => Set<ManagementAudit>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<StationProfile>().ToTable("StationProfiles");
        model.Entity<TraceAccount>().ToTable("TraceAccounts");
        model.Entity<ManagementAudit>().ToTable("ManagementAudit");
    }

    public async Task MigrateAsync()
    {
        await using var tx = await Database.BeginTransactionAsync();
        await Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS ManagementSchema (Id INTEGER PRIMARY KEY CHECK(Id=1), Version INTEGER NOT NULL);
            INSERT OR IGNORE INTO ManagementSchema VALUES(1,0);
            """);
        var version = await Database.SqlQueryRaw<int>("SELECT Version AS Value FROM ManagementSchema WHERE Id=1").SingleAsync();
        if (version is not (0 or 1)) throw new InvalidDataException("Unsupported management schema version.");
        if (version == 0)
        {
            await Database.ExecuteSqlRawAsync("""
                CREATE TABLE StationProfiles (Code TEXT PRIMARY KEY, Name TEXT NOT NULL, Enabled INTEGER NOT NULL, Version INTEGER NOT NULL);
                CREATE TABLE TraceAccounts (UserName TEXT PRIMARY KEY, PasswordHash TEXT NOT NULL, Role TEXT NOT NULL, Enabled INTEGER NOT NULL, Version INTEGER NOT NULL);
                CREATE TABLE ManagementAudit (Id INTEGER PRIMARY KEY AUTOINCREMENT, Actor TEXT NOT NULL, Action TEXT NOT NULL, Target TEXT NOT NULL, At TEXT NOT NULL);
                UPDATE ManagementSchema SET Version=1 WHERE Id=1;
                """);
        }
        await tx.CommitAsync();
    }

    public async Task CheckSchemaAsync()
    {
        try
        {
            var version = await Database.SqlQueryRaw<int>("SELECT Version AS Value FROM ManagementSchema WHERE Id=1").SingleAsync();
            if (version != 1) throw new InvalidDataException("Unsupported management schema version.");
            await Stations.AsNoTracking().Select(s => s.Code).Take(1).ToListAsync();
            await Accounts.AsNoTracking().Select(a => a.UserName).Take(1).ToListAsync();
            await Audit.AsNoTracking().Select(a => a.Id).Take(1).ToListAsync();
        }
        catch (SqliteException error) when (error.SqliteErrorCode == 1)
        { throw new InvalidOperationException("Run --Management:Enabled true --Management:Migrate true first.", error); }
    }

    public void Record(string actor, string action, string target) =>
        Audit.Add(new ManagementAudit { Actor = actor, Action = action, Target = target, At = DateTimeOffset.UtcNow });

    public async Task BootstrapAsync(string? user, string? password)
    {
        if (!ManagementEndpoints.ValidCode(user) || !ManagementEndpoints.ValidPassword(password))
            throw new ArgumentException("Bootstrap requires a valid user and a password of 12..256 characters.");
        await using var tx = await Database.BeginTransactionAsync();
        if (await Accounts.AnyAsync()) throw new InvalidOperationException("Bootstrap is only allowed for an empty account table.");
        var account = new TraceAccount { UserName = user!, Role = "admin", Enabled = true, Version = 1 };
        account.PasswordHash = new PasswordHasher<TraceAccount>().HashPassword(account, password!);
        Accounts.Add(account);
        Record("bootstrap", "create-admin", user!);
        await SaveChangesAsync();
        await tx.CommitAsync();
    }
}

sealed class StationProfile
{
    [Key] public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    [ConcurrencyCheck] public long Version { get; set; }
}
sealed class TraceAccount
{
    [Key] public string UserName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "viewer";
    public bool Enabled { get; set; }
    [ConcurrencyCheck] public long Version { get; set; }
}
sealed class ManagementAudit
{
    [Key] public long Id { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

sealed class ManagementReadiness(IServiceScopeFactory scopes) : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ManagementDb>().CheckSchemaAsync();
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy();
        }
        catch (Exception error) when (error is SqliteException or InvalidDataException or InvalidOperationException)
        { return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Management schema unavailable"); }
    }
}
