using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

static class InspectionPages
{
    private sealed record Scope(string Station, string Result, long? Start, long? End, string? ReviewStatus = null);
    private sealed record Cursor(int Version, Scope Scope, long Ticks, string Id);
    private sealed record Row(string Id, string Station, long ImportedTicks, string Result, double RoiMean)
    {
        public InspectionSummary ToSummary() =>
            new(Id, Station, new DateTime(ImportedTicks, DateTimeKind.Utc), Result, RoiMean);
    }

    public static void MapInspectionPages(this WebApplication app, bool readLimitEnabled)
    {
        var endpoint = app.MapGet("/api/inspection-pages", ReadAsync).WithName("ListInspectionCursorPage")
            .Produces<InspectionCursorPage>().ProducesProblem(400).ProducesProblem(429).ProducesProblem(500);
        if (readLimitEnabled) endpoint.RequireRateLimiting(ReadRateLimit.Policy);
    }

    private static async Task<IResult> ReadAsync(
        string station, string result, string? since, string? until, int? pageSize, string? after, string? reviewStatus,
        TraceDb db, IConfiguration configuration, CancellationToken ct)
    {
        int size = pageSize ?? 20;
        if (size is < 1 or > 100)
            return Results.Problem(statusCode: 400, title: "Invalid pageSize");
        if (string.IsNullOrWhiteSpace(station) || station.Length > 64 || station.Any(char.IsControl)
            || result is not ("OK" or "NG"))
            return Results.Problem(statusCode: 400, title: "Specify a station and an OK or NG result");
        if (!TimeRange.TryReadUtc(since, out var start) || !TimeRange.TryReadUtc(until, out var end)
            || (start.HasValue && end.HasValue && start >= end))
            return Results.Problem(statusCode: 400, title: "Use UTC timestamps and an increasing time range");

        reviewStatus = string.IsNullOrEmpty(reviewStatus) ? null : reviewStatus;
        if (reviewStatus is not null && (!configuration.GetValue<bool>("Review:Enabled")
            || reviewStatus is not ("pending" or "reviewed")))
            return Results.Problem(statusCode: 400, title: "Enable review and use pending or reviewed");

        var scope = new Scope(station, result, start, end, reviewStatus);
        Cursor? boundary = null;
        if (after is not null)
        {
            boundary = ReadCursor(after);
            if (boundary is null || boundary.Scope != scope)
                return Results.Problem(statusCode: 400, title: "Invalid cursor for this query");
        }

        string sql = """
            SELECT Id, Station, ImportedTicks, Result, RoiMean
            FROM Inspections WHERE Station = $station AND Result = $result
            """;
        var parameters = new List<SqliteParameter>
        {
            new("$station", station), new("$result", result), new("$limit", size + 1)
        };
        if (reviewStatus is not null)
        {
            sql += " AND Result = 'NG' AND (EXISTS (SELECT 1 FROM Reviews WHERE InspectionId = Inspections.Id)) = $reviewed";
            parameters.Add(new("$reviewed", reviewStatus == "reviewed" ? 1 : 0));
        }
        if (start is long lower)
        {
            sql += " AND ImportedTicks >= $start";
            parameters.Add(new("$start", lower));
        }
        if (end is long upper)
        {
            sql += " AND ImportedTicks < $end";
            parameters.Add(new("$end", upper));
        }
        if (boundary is not null)
        {
            sql += " AND (ImportedTicks, Id) < ($ticks, $id)";
            parameters.Add(new("$ticks", boundary.Ticks));
            parameters.Add(new("$id", boundary.Id));
        }
        sql += " ORDER BY ImportedTicks DESC, Id DESC LIMIT $limit";

        var rows = await db.Database.SqlQueryRaw<Row>(sql, parameters.ToArray()).ToListAsync(ct);
        bool hasMore = rows.Count > size;
        string? nextCursor = hasMore
            ? WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(
                new Cursor(1, scope, rows[size - 1].ImportedTicks, rows[size - 1].Id)))
            : null;
        return Results.Ok(new InspectionCursorPage(rows.Take(size).Select(row => row.ToSummary()).ToArray(), nextCursor));
    }

    private static Cursor? ReadCursor(string after)
    {
        if (after.Length is < 1 or > 2048) return null;
        try
        {
            var cursor = JsonSerializer.Deserialize<Cursor>(WebEncoders.Base64UrlDecode(after));
            return cursor is { Version: 1, Id.Length: > 0, Ticks: >= 0 }
                && cursor.Ticks <= DateTime.MaxValue.Ticks ? cursor : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

sealed record InspectionCursorPage(IReadOnlyList<InspectionSummary> Items, string? NextCursor);
