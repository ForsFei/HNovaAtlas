using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

static class ReviewEndpoints
{
    private const string Reviewer = "reviewer";

    public static void AddReviewServices(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "QualityTrace.Session";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TimeSpan.FromHours(1);
            options.SlidingExpiration = false;
        });
        services.AddAuthorization();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-Token";
            options.Cookie.Name = "QualityTrace.Csrf";
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
    }

    public static async Task CreateReviewTableAsync(TraceDb db)
    {
        // Additive bootstrap for the database created by lesson 03.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS Reviews (
                InspectionId TEXT NOT NULL PRIMARY KEY REFERENCES Inspections(Id),
                Decision TEXT NOT NULL,
                Note TEXT NOT NULL,
                Reviewer TEXT NOT NULL,
                ReviewedAt TEXT NOT NULL
            )
            """);
    }

    public static void MapReviewEndpoints(this WebApplication app)
    {
        bool managed = app.Configuration.GetValue<bool>("Management:Enabled");
        var accounts = managed ? null : new Dictionary<string, (byte[] Secret, string Role)>(StringComparer.Ordinal)
        {
            ["alice"] = (ReadSecret(app.Configuration, "Alice"), Reviewer),
            ["bob"] = (ReadSecret(app.Configuration, "Bob"), Reviewer),
            ["viewer"] = (ReadSecret(app.Configuration, "Viewer"), "viewer")
        };

        var api = app.MapGroup("/api");
        api.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            http.Response.Headers.CacheControl = "no-store";
            if (http.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote))
                return Results.StatusCode(403);
            if (HttpMethods.IsPost(http.Request.Method))
            {
                var antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();
                try { await antiforgery.ValidateRequestAsync(http); }
                catch (AntiforgeryValidationException)
                {
                    return Results.Problem(statusCode: 400, title: "Invalid CSRF token");
                }
            }
            return await next(context);
        });

        api.MapGet("/session", (HttpContext http, IAntiforgery antiforgery) => TypedResults.Ok(new
        {
            user = http.User.Identity?.Name,
            canReview = http.User.IsInRole(Reviewer) || http.User.IsInRole("admin"),
            csrf = antiforgery.GetAndStoreTokens(http).RequestToken
        })).WithName("GetSession").ProducesProblem(403).ProducesProblem(500);

        var login = api.MapPost("/session/login", async Task<IResult> (Login input, HttpContext http) =>
        {
            string role;
            long? accountVersion = null;
            if (managed)
            {
                var db = http.RequestServices.GetRequiredService<ManagementDb>();
                var account = await ManagementEndpoints.AuthenticateAsync(db, input.User, input.Password);
                if (account is null) return Results.Unauthorized();
                role = account.Role; accountVersion = account.Version;
            }
            else
            {
                if (input.User is null || input.Password is null ||
                    !accounts!.TryGetValue(input.User, out var account) ||
                    !CryptographicOperations.FixedTimeEquals(Hash(input.Password), account.Secret))
                    return Results.Unauthorized();
                role = account.Role;
            }
            var claims = new List<Claim> {
                new(ClaimTypes.NameIdentifier, input.User!), new(ClaimTypes.Name, input.User!), new(ClaimTypes.Role, role)
            };
            if (accountVersion is { } version) claims.Add(new("accountVersion", version.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(new ClaimsPrincipal(identity));
            return Results.NoContent();
        }).WithName("Login").Produces(204).ProducesProblem(400).ProducesProblem(401)
          .ProducesProblem(403).ProducesProblem(500);
        if (managed) login.RequireRateLimiting("management-login");

        api.MapPost("/session/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync();
            return TypedResults.NoContent();
        }).RequireAuthorization().DisableCookieRedirect().WithName("Logout")
          .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(500);

        api.MapGet("/inspections/{id}/review", async (string id, TraceDb db, CancellationToken ct) =>
            TypedResults.Ok(new
            {
                review = await db.Reviews.AsNoTracking().SingleOrDefaultAsync(row => row.InspectionId == id, ct)
            }))
            .RequireAuthorization().WithName("GetReview")
            .WithDescription("Authenticated users may read review history. A missing review returns { review: null }.")
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(500);

        api.MapPost("/inspections/{id}/review", async Task<IResult> (
            string id, ReviewInput input, HttpContext http, TraceDb db, CancellationToken ct) =>
        {
            string note = input.Note?.Trim() ?? "";
            var errors = new Dictionary<string, string[]>();
            if (input.Decision is not ("confirmed-ng" or "false-reject"))
                errors["decision"] = ["请选择确认 NG 或误判。"];
            if (note.Length is < 1 or > 500)
                errors["note"] = ["说明去除首尾空白后须为 1～500 个字符。"];
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var result = await db.Inspections.Where(row => row.Id == id).Select(row => row.Result)
                .SingleOrDefaultAsync(ct);
            if (result is null) return Results.NotFound();
            if (result != "NG") return Results.Problem(statusCode: 409, title: "Only NG cycles can be reviewed");

            var review = new InspectionReview(id, input.Decision!, note, http.User.Identity!.Name!, DateTimeOffset.UtcNow);
            db.Reviews.Add(review);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 })
            {
                return Results.Problem(statusCode: 409, title: "Cycle already reviewed");
            }

            return Results.Created($"/api/inspections/{id}/review", review);
        }).RequireAuthorization(policy => policy.RequireRole(Reviewer, "admin")).WithName("SubmitReview")
          .WithDescription("Requires reviewer role. Decision is confirmed-ng or false-reject; trimmed note length is 1..500. Only NG cycles accept one review. Existing review or OK result returns 409.")
          .Produces<InspectionReview>(201).ProducesProblem(400).ProducesProblem(401)
          .ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(500);
    }

    private static byte[] ReadSecret(IConfiguration config, string name) =>
        config[$"Review:{name}"] is { Length: >= 12 } secret ? Hash(secret) :
        throw new InvalidOperationException($"Set Review__{name} to a local practice password of at least 12 characters.");

    private static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    private sealed record Login(string? User, string? Password);
    private sealed record ReviewInput(string? Decision, string? Note);
}
